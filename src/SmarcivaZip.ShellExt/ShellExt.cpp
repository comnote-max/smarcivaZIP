// smarcivaZIP のストア版（MSIX）用の右クリックメニュー。
//
// Windows 11 の最初の右クリックメニューに項目を出すには、パッケージに IExplorerCommand を
// 実装した COM クラスを同梱し、AppxManifest の desktop4:FileExplorerContextMenus で登録する。
// このクラスは com:SurrogateServer として、エクスプローラーとは別の dllhost.exe の中で動く。
// ここで不具合が起きてもエクスプローラーは巻き込まれない。
//
// 役目は「メニューに項目を並べる」と「選ばれたら SmarcivaZip.exe を起動する」の 2 つだけ。
// どの圧縮形式を何語で並べるかはアプリが決めて LocalState\menu.tsv に書いておく
// （SmarcivaZip.Core/Shell/ModernMenuFile.cs）。ここはそれを読むだけで、翻訳も設定も持たない。
// アプリを一度も起動していなくてもメニューが空にならないよう、最低限の既定値だけは持つ。

#include <windows.h>
#include <shobjidl_core.h>
#include <shlobj_core.h>
#include <shlwapi.h>
#include <appmodel.h>
#include <new>
#include <string>
#include <vector>

// {F25B0869-E77F-4627-AAA1-A00B930517F8}  AppxManifest の com:Class と desktop5:Verb に同じ値を書く。
static const CLSID CLSID_SmarcivaZipMenu =
    { 0xf25b0869, 0xe77f, 0x4627, { 0xaa, 0xa1, 0xa0, 0x0b, 0x93, 0x05, 0x17, 0xf8 } };

static HMODULE g_module = nullptr;
static LONG g_objects = 0;
static LONG g_locks = 0;

namespace
{
    struct MenuItem
    {
        std::wstring kind;   // compress / extract / settings / separator
        std::wstring title;
        std::wstring args;
    };

    struct Menu
    {
        std::vector<std::wstring> extensions;   // 解凍の項目を出す拡張子（小文字・ドット無し）
        std::vector<MenuItem> items;
    };

    // ------------------------------------------------------------------ 文字列の小道具

    std::wstring Lower(std::wstring value)
    {
        CharLowerBuffW(value.data(), static_cast<DWORD>(value.size()));
        return value;
    }

    std::wstring Utf8ToWide(const std::string& bytes)
    {
        if (bytes.empty()) return {};
        int length = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
        std::wstring wide(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), wide.data(), length);
        return wide;
    }

    std::string WideToUtf8(const std::wstring& wide)
    {
        if (wide.empty()) return {};
        int length = WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
        std::string bytes(length, '\0');
        WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), bytes.data(), length, nullptr, nullptr);
        return bytes;
    }

    std::vector<std::wstring> Split(const std::wstring& text, wchar_t separator)
    {
        std::vector<std::wstring> parts;
        size_t start = 0;
        for (;;)
        {
            size_t end = text.find(separator, start);
            parts.push_back(text.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start));
            if (end == std::wstring::npos) break;
            start = end + 1;
        }
        return parts;
    }

    // ------------------------------------------------------------------ 場所

    /// このDLLが置かれているフォルダ（= パッケージのインストール先）。SmarcivaZip.exe も同じ場所にある。
    std::wstring ModuleDirectory()
    {
        wchar_t path[MAX_PATH * 4];
        DWORD length = GetModuleFileNameW(g_module, path, ARRAYSIZE(path));
        std::wstring result(path, length);
        size_t slash = result.find_last_of(L"\\/");
        return slash == std::wstring::npos ? std::wstring() : result.substr(0, slash);
    }

    std::wstring ExecutablePath() { return ModuleDirectory() + L"\\SmarcivaZip.exe"; }

    /// %LOCALAPPDATA%\Packages\<ファミリー名>\LocalState\menu.tsv。アプリ側の PackageContext と同じ場所。
    std::wstring MenuFilePath()
    {
        UINT32 length = 0;
        if (GetCurrentPackageFamilyName(&length, nullptr) != ERROR_INSUFFICIENT_BUFFER || length == 0)
            return {};

        std::wstring family(length, L'\0');
        if (GetCurrentPackageFamilyName(&length, family.data()) != ERROR_SUCCESS) return {};
        family.resize(length > 0 ? length - 1 : 0);

        PWSTR localAppData = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData))) return {};
        std::wstring path = std::wstring(localAppData) + L"\\Packages\\" + family + L"\\LocalState\\menu.tsv";
        CoTaskMemFree(localAppData);
        return path;
    }

    // ------------------------------------------------------------------ メニューの中身

    /// アプリを一度も起動していないときの既定値。日本語環境なら日本語、それ以外は英語。
    Menu DefaultMenu()
    {
        bool japanese = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_JAPANESE;
        Menu menu;
        menu.extensions = { L"zip", L"7z", L"rar", L"tar", L"gz", L"tgz", L"bz2", L"tbz", L"xz", L"txz",
                            L"zst", L"tzst", L"lzh", L"lha", L"cab", L"arj", L"z", L"lzma" };
        menu.items = {
            { L"compress", japanese ? L"ZIP に圧縮" : L"Compress to ZIP", L"--compress zip" },
            { L"compress", japanese ? L"7z に圧縮" : L"Compress to 7z", L"--compress 7z" },
            { L"separator", L"", L"" },
            { L"extract", japanese ? L"ここに解凍" : L"Extract here", L"--extract" },
            { L"extract", japanese ? L"フォルダを作って解凍" : L"Extract to a new folder", L"--extract-to-folder" },
            { L"extract", japanese ? L"文字コードを選んで解凍..." : L"Extract, choosing the encoding...", L"--extract-preview" },
            { L"separator", L"", L"" },
            { L"settings", japanese ? L"設定..." : L"Settings...", L"--settings" },
        };
        return menu;
    }

    /// menu.tsv を読む。読めない・壊れているときは既定値にする（メニューが消えるよりはよい）。
    Menu LoadMenu()
    {
        std::wstring path = MenuFilePath();
        if (path.empty()) return DefaultMenu();

        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                  nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return DefaultMenu();

        std::string bytes;
        LARGE_INTEGER size{};
        if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart < 1024 * 1024)
        {
            bytes.resize(static_cast<size_t>(size.QuadPart));
            DWORD read = 0;
            if (!ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr)) bytes.clear();
            bytes.resize(read);
        }
        CloseHandle(file);

        Menu menu;
        bool headerSeen = false;

        for (std::wstring line : Split(Utf8ToWide(bytes), L'\n'))
        {
            if (!line.empty() && line.back() == L'\r') line.pop_back();
            if (line.empty()) continue;

            if (line[0] == L'#') { headerSeen = headerSeen || line.find(L"smarcivaZIP context menu v1") != std::wstring::npos; continue; }

            std::vector<std::wstring> fields = Split(line, L'\t');
            const std::wstring& kind = fields[0];

            if (kind == L"extensions" && fields.size() >= 2)
            {
                for (const std::wstring& extension : Split(fields[1], L';'))
                    if (!extension.empty()) menu.extensions.push_back(Lower(extension));
            }
            else if (kind == L"separator")
            {
                menu.items.push_back({ kind, L"", L"" });
            }
            else if ((kind == L"compress" || kind == L"extract" || kind == L"settings") && fields.size() >= 3)
            {
                menu.items.push_back({ kind, fields[1], fields[2] });
            }
        }

        // 見出しが無い・項目が無いファイルは、書きかけか別物とみなす。
        if (!headerSeen || menu.items.empty()) return DefaultMenu();
        return menu;
    }

    // ------------------------------------------------------------------ 選択されたもの

    std::vector<std::wstring> SelectedPaths(IShellItemArray* items)
    {
        std::vector<std::wstring> paths;
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count))) return paths;

        for (DWORD i = 0; i < count; i++)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item))) continue;

            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            {
                paths.emplace_back(path);
                CoTaskMemFree(path);
            }
            item->Release();
        }
        return paths;
    }

    /// 選ばれたものがすべて、解凍できそうなファイルか。フォルダが混ざっていれば false。
    bool AllAreArchives(IShellItemArray* items, const std::vector<std::wstring>& extensions)
    {
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count)) || count == 0) return false;

        for (DWORD i = 0; i < count; i++)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item))) return false;

            SFGAOF attributes = 0;
            bool isFolder = SUCCEEDED(item->GetAttributes(SFGAO_FOLDER | SFGAO_STREAM, &attributes))
                            && (attributes & SFGAO_FOLDER) && !(attributes & SFGAO_STREAM);

            PWSTR name = nullptr;
            bool matches = false;
            if (!isFolder && SUCCEEDED(item->GetDisplayName(SIGDN_PARENTRELATIVEPARSING, &name)))
            {
                std::wstring file = Lower(name);
                CoTaskMemFree(name);

                size_t dot = file.find_last_of(L'.');
                if (dot != std::wstring::npos)
                {
                    std::wstring extension = file.substr(dot + 1);
                    for (const std::wstring& candidate : extensions)
                        if (candidate == extension) { matches = true; break; }
                }
            }
            item->Release();

            if (!matches) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 起動

    std::wstring Quote(const std::wstring& value) { return L"\"" + value + L"\""; }

    HRESULT Launch(const std::wstring& arguments)
    {
        std::wstring exe = ExecutablePath();
        std::wstring commandLine = Quote(exe) + L" " + arguments;

        STARTUPINFOW startup{ sizeof(startup) };
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
            return HRESULT_FROM_WIN32(GetLastError());

        // エクスプローラーの前面にアプリの画面を出せるようにする。
        AllowSetForegroundWindow(process.dwProcessId);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    /// 選ばれたパスを 1 行 1 パスで一時ファイルに書き、--paths-from で渡す。
    /// 数百個を選ぶとコマンドラインの長さ（32767 文字）を超えるため。アプリは読み終えたら消す。
    HRESULT LaunchWithPaths(const std::wstring& arguments, const std::vector<std::wstring>& paths)
    {
        if (paths.empty()) return S_OK;

        wchar_t tempDirectory[MAX_PATH + 1];
        if (!GetTempPathW(ARRAYSIZE(tempDirectory), tempDirectory)) return HRESULT_FROM_WIN32(GetLastError());

        GUID id{};
        CoCreateGuid(&id);
        wchar_t idText[64];
        StringFromGUID2(id, idText, ARRAYSIZE(idText));
        std::wstring listPath = std::wstring(tempDirectory) + L"smarcivazip-" + idText + L".txt";

        std::string content;
        for (const std::wstring& path : paths) content += WideToUtf8(path) + "\n";

        HANDLE file = CreateFileW(listPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        DWORD written = 0;
        BOOL ok = WriteFile(file, content.data(), static_cast<DWORD>(content.size()), &written, nullptr);
        CloseHandle(file);
        if (!ok) { DeleteFileW(listPath.c_str()); return HRESULT_FROM_WIN32(GetLastError()); }

        HRESULT hr = Launch(arguments + L" --paths-from " + Quote(listPath));
        if (FAILED(hr)) DeleteFileW(listPath.c_str());
        return hr;
    }

    // ------------------------------------------------------------------ COM の実装

    template <typename Interface>
    class RefCounted : public Interface
    {
    public:
        RefCounted() { InterlockedIncrement(&g_objects); }
        virtual ~RefCounted() { InterlockedDecrement(&g_objects); }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_refs); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG refs = InterlockedDecrement(&m_refs);
            if (refs == 0) delete this;
            return refs;
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
        {
            if (!result) return E_POINTER;
            if (riid == IID_IUnknown || riid == __uuidof(Interface))
            {
                *result = static_cast<Interface*>(this);
                AddRef();
                return S_OK;
            }
            *result = nullptr;
            return E_NOINTERFACE;
        }

    private:
        LONG m_refs = 1;
    };

    class EnumCommands final : public RefCounted<IEnumExplorerCommand>
    {
    public:
        explicit EnumCommands(std::vector<IExplorerCommand*> commands) : m_commands(std::move(commands)) {}

        ~EnumCommands() override
        {
            for (IExplorerCommand* command : m_commands) command->Release();
        }

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
        {
            ULONG given = 0;
            while (given < count && m_index < m_commands.size())
            {
                commands[given] = m_commands[m_index++];
                commands[given]->AddRef();
                given++;
            }
            if (fetched) *fetched = given;
            return given == count ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Skip(ULONG count) override
        {
            m_index = (std::min)(m_index + count, m_commands.size());
            return m_index < m_commands.size() ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Reset() override { m_index = 0; return S_OK; }

        IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override { *result = nullptr; return E_NOTIMPL; }

    private:
        std::vector<IExplorerCommand*> m_commands;
        size_t m_index = 0;
    };

    /// サブメニューの 1 項目。
    class ItemCommand final : public RefCounted<IExplorerCommand>
    {
    public:
        ItemCommand(MenuItem item, std::vector<std::wstring> extensions)
            : m_item(std::move(item)), m_extensions(std::move(extensions)) {}

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override { return SHStrDupW(m_item.title.c_str(), title); }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override { *icon = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = GUID_NULL; return E_NOTIMPL; }

        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            // 解凍の項目は、選ばれたものがすべて書庫のときだけ出す。
            *state = (m_item.kind == L"extract" && !AllAreArchives(items, m_extensions)) ? ECS_HIDDEN : ECS_ENABLED;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            if (m_item.kind == L"separator") return S_OK;
            if (m_item.kind == L"settings") return Launch(m_item.args);
            return LaunchWithPaths(m_item.args, SelectedPaths(items));
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            *flags = m_item.kind == L"separator" ? ECF_ISSEPARATOR : ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** result) override { *result = nullptr; return E_NOTIMPL; }

    private:
        MenuItem m_item;
        std::vector<std::wstring> m_extensions;
    };

    /// 最初のメニューに出る「smarcivaZIP ▸」。中身はサブメニューとして開く。
    class RootCommand final : public RefCounted<IExplorerCommand>
    {
    public:
        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override { return SHStrDupW(L"smarcivaZIP", title); }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            return SHStrDupW((ExecutablePath() + L",0").c_str(), icon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = CLSID_SmarcivaZipMenu; return S_OK; }
        IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = ECS_ENABLED; return S_OK; }
        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return E_NOTIMPL; }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_HASSUBCOMMANDS; return S_OK; }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** result) override
        {
            // 開くたびに読み直す。設定や表示言語を変えたあと、エクスプローラーを再起動しなくても反映される。
            Menu menu = LoadMenu();

            std::vector<IExplorerCommand*> commands;
            for (const MenuItem& item : menu.items)
            {
                ItemCommand* command = new (std::nothrow) ItemCommand(item, menu.extensions);
                if (command) commands.push_back(command);
            }

            EnumCommands* enumerator = new (std::nothrow) EnumCommands(std::move(commands));
            if (!enumerator) { *result = nullptr; return E_OUTOFMEMORY; }
            *result = enumerator;
            return S_OK;
        }
    };

    class ClassFactory final : public RefCounted<IClassFactory>
    {
    public:
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** result) override
        {
            *result = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;

            RootCommand* command = new (std::nothrow) RootCommand();
            if (!command) return E_OUTOFMEMORY;
            HRESULT hr = command->QueryInterface(riid, result);
            command->Release();
            return hr;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks);
            return S_OK;
        }
    };
}

// ---------------------------------------------------------------------- DLL の入口

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != CLSID_SmarcivaZipMenu) return CLASS_E_CLASSNOTAVAILABLE;

    ClassFactory* factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, result);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return (g_objects == 0 && g_locks == 0) ? S_OK : S_FALSE;
}
