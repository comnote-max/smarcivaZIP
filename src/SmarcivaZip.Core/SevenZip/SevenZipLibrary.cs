using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SmarcivaZip.Core.SevenZip;

public sealed class SevenZipNotFoundException(string message) : Exception(message);

/// <summary>
/// 7z.dll を動的に読み込み、ハンドラを列挙して COM オブジェクトを生成する。
/// COM 登録は不要（レジストリフリー）。プロセス内で 1 インスタンスを共有する。
/// </summary>
public sealed class SevenZipLibrary : IDisposable
{
    private static readonly Lazy<SevenZipLibrary> LazyInstance =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static SevenZipLibrary Instance => LazyInstance.Value;

    private readonly IntPtr _module;
    private readonly NativeMethods.CreateObjectDelegate _createObject;
    private List<HandlerInfo>? _handlers;

    public string LibraryPath { get; }

    private SevenZipLibrary(IntPtr module, string path)
    {
        _module = module;
        LibraryPath = path;
        _createObject = GetExport<NativeMethods.CreateObjectDelegate>("CreateObject");

        // 大きな辞書サイズを使う際のメモリ確保を速くする。権限が無ければ失敗するが無害。
        try
        {
            GetExport<NativeMethods.SetLargePageModeDelegate>("SetLargePageMode")();
        }
        catch (EntryPointNotFoundException)
        {
            // 古い 7z.dll にはこのエクスポートが無い。
        }
    }

    private T GetExport<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_module, name, out IntPtr address))
            throw new EntryPointNotFoundException("7z.dll に " + name + " が見つかりません。");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static SevenZipLibrary Load()
    {
        foreach (string candidate in EnumerateCandidatePaths())
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                IntPtr module = NativeLibrary.Load(candidate);
                return new SevenZipLibrary(module, candidate);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // アーキテクチャ不一致（32bit の 7z.dll など）。次の候補へ。
            }
        }

        throw new SevenZipNotFoundException(
            "7z.dll が見つかりませんでした。" + Environment.NewLine + Environment.NewLine +
            "smarcivaZIP と同じフォルダに 7z.dll を置くか、7-Zip をインストールしてください。" +
            Environment.NewLine + "https://www.7-zip.org/");
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        string appDir = AppContext.BaseDirectory;
        yield return Path.Combine(appDir, "7z.dll");
        yield return Path.Combine(appDir,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "7z.dll");

        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            string? installDir = TryReadSevenZipInstallDir(view);
            if (!string.IsNullOrEmpty(installDir))
                yield return Path.Combine(installDir, "7z.dll");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.dll");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.dll");
    }

    private static string? TryReadSevenZipInstallDir(RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(@"SOFTWARE\7-Zip");
            return key?.GetValue("Path64") as string ?? key?.GetValue("Path") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public IReadOnlyList<HandlerInfo> Handlers => _handlers ??= EnumerateHandlers();

    private List<HandlerInfo> EnumerateHandlers()
    {
        var getCount = GetExport<NativeMethods.GetNumberOfFormatsDelegate>("GetNumberOfFormats");
        var getProperty = GetExport<NativeMethods.GetHandlerProperty2Delegate>("GetHandlerProperty2");

        getCount(out uint count);
        var result = new List<HandlerInfo>((int)count);

        for (uint i = 0; i < count; i++)
        {
            Guid classId = ReadGuid(getProperty, i, HandlerPropId.ClassId);
            if (classId == Guid.Empty) continue;

            result.Add(new HandlerInfo
            {
                Index = i,
                Name = ReadString(getProperty, i, HandlerPropId.Name) ?? ("Format" + i),
                ClassId = classId,
                Extensions = SplitExtensions(ReadString(getProperty, i, HandlerPropId.Extension)),
                AddExtensions = SplitExtensions(ReadString(getProperty, i, HandlerPropId.AddExtension)),
                CanUpdate = ReadBool(getProperty, i, HandlerPropId.Update),
                Signatures = ReadSignatures(getProperty, i),
                SignatureOffset = ReadUInt32(getProperty, i, HandlerPropId.SignatureOffset),
                Flags = (ArchiveFlags)ReadUInt32(getProperty, i, HandlerPropId.Flags)
            });
        }

        return result;
    }

    private static string? ReadString(
        NativeMethods.GetHandlerProperty2Delegate get, uint index, HandlerPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try { return get(index, id, ref value) != 0 ? null : value.AsString(); }
        finally { value.Clear(); }
    }

    private static Guid ReadGuid(
        NativeMethods.GetHandlerProperty2Delegate get, uint index, HandlerPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try { return get(index, id, ref value) != 0 ? Guid.Empty : value.AsGuid(); }
        finally { value.Clear(); }
    }

    private static bool ReadBool(
        NativeMethods.GetHandlerProperty2Delegate get, uint index, HandlerPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try { return get(index, id, ref value) == 0 && value.AsBool(); }
        finally { value.Clear(); }
    }

    private static uint ReadUInt32(
        NativeMethods.GetHandlerProperty2Delegate get, uint index, HandlerPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try { return get(index, id, ref value) != 0 ? 0u : value.AsUInt32(); }
        finally { value.Clear(); }
    }

    /// <summary>
    /// 単一シグネチャは kSignature に、複数持つ形式は kMultiSignature に
    /// [長さ 1 バイト][バイト列] の連結として格納されている。
    /// </summary>
    private static IReadOnlyList<byte[]> ReadSignatures(
        NativeMethods.GetHandlerProperty2Delegate get, uint index)
    {
        var list = new List<byte[]>();

        PropVariant multi = PropVariant.Empty;
        try
        {
            if (get(index, HandlerPropId.MultiSignature, ref multi) == 0 && !multi.IsEmpty)
            {
                byte[] packed = multi.AsBytes();
                int position = 0;
                while (position < packed.Length)
                {
                    int length = packed[position++];
                    if (length == 0 || position + length > packed.Length) break;
                    list.Add(packed[position..(position + length)]);
                    position += length;
                }
            }
        }
        finally { multi.Clear(); }

        if (list.Count > 0) return list;

        PropVariant single = PropVariant.Empty;
        try
        {
            if (get(index, HandlerPropId.Signature, ref single) == 0 && !single.IsEmpty)
            {
                byte[] bytes = single.AsBytes();
                if (bytes.Length > 0) list.Add(bytes);
            }
        }
        finally { single.Clear(); }

        return list;
    }

    private static string[] SplitExtensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(e => e != "*")
                  .Select(e => e.TrimStart('.').ToLowerInvariant())
                  .Where(e => e.Length > 0)
                  .Distinct(StringComparer.Ordinal)
                  .ToArray();
    }

    public HandlerInfo? FindHandler(string name)
        => Handlers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    private T CreateObject<T>(Guid classId) where T : class
    {
        Guid interfaceId = typeof(T).GUID;
        int hr = _createObject(in classId, in interfaceId, out IntPtr raw);

        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        if (raw == IntPtr.Zero) throw new InvalidOperationException("7z.dll がハンドラを生成できませんでした。");

        try { return (T)Marshal.GetTypedObjectForIUnknown(raw, typeof(T)); }
        finally { Marshal.Release(raw); }
    }

    public IInArchive CreateInArchive(Guid classId) => CreateObject<IInArchive>(classId);

    public IOutArchive CreateOutArchive(Guid classId) => CreateObject<IOutArchive>(classId);

    public void Dispose()
    {
        if (_module != IntPtr.Zero) NativeLibrary.Free(_module);
    }
}
