; smarcivaZIP のインストーラ定義（Inno Setup 6）
;
; ビルドは tools/build.ps1 から行う。単体で試すなら:
;   ISCC.exe /DAppVersion=1.0.0 /DArch=x64 installer\smarcivaZIP.iss
;
; 方針
; - 利用者ごとのインストール（管理者権限なし）。関連付けと右クリックメニューを
;   HKCU にしか書かないので、全ユーザー向けに入れても登録されるのは入れた本人だけになる。
;   できないことを選ばせないため、全ユーザー向けは用意しない。
; - 登録・解除はアプリ自身の --register / --unregister に任せる。
;   同じ処理を二か所に書くと、片方だけ直して食い違うため。
; - アンインストール時の解除は、設定ファイルの一覧ではなくレジストリを総当たりする
;   （ShellRegistration.Unregister）。一覧から外した拡張子も取り残さない。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

#define RepoRoot AddBackslash(SourcePath) + ".."
#ifndef SourceDir
  #define SourceDir RepoRoot + "\dist\win-" + Arch
#endif

#define AppName "smarcivaZIP"
#define AppExe "SmarcivaZip.exe"

[Setup]
; AppId はアップグレードの判定に使う。一度公開したら二度と変えないこと。
AppId={{6910F0A7-D092-4652-BA60-873CF3FF0DBB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=smarciva
AppPublisherURL=https://smarciva.com/
AppSupportURL=https://github.com/comnote-max/smarcivaZIP/issues
AppUpdatesURL=https://github.com/comnote-max/smarcivaZIP/releases
AppCopyright=Copyright (c) 2026 smarcivaZIP contributors
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
; .NET 9 の下限に合わせる（Windows 10 1607）。
MinVersion=10.0.14393

LicenseFile=license\LICENSE-en.txt
SetupIconFile={#RepoRoot}\src\SmarcivaZip.App\Assets\smarcivazip.ico
WizardStyle=modern
WizardSmallImageFile=images\small-100.bmp,images\small-150.bmp,images\small-200.bmp
WizardImageFile=images\large-100.bmp,images\large-150.bmp,images\large-200.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; 解凍や圧縮の途中で上書き・削除すると壊れるので、動いていれば閉じてもらう。
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

OutputDir={#RepoRoot}\dist
OutputBaseFilename={#AppName}-{#AppVersion}-setup-{#Arch}
; 本体は単一ファイル発行の段階で圧縮済みなので、ここで粘っても大して縮まない。
Compression=lzma2/max
SolidCompression=yes

; 画面の言語は Windows の表示言語から選ぶ。合うものが無ければ英語。
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "english";             MessagesFile: "compiler:Default.isl"
Name: "japanese";            MessagesFile: "compiler:Languages\Japanese.isl"; LicenseFile: "license\LICENSE-ja.txt"
Name: "spanish";             MessagesFile: "compiler:Languages\Spanish.isl"
Name: "arabic";              MessagesFile: "compiler:Languages\Arabic.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "french";              MessagesFile: "compiler:Languages\French.isl"
Name: "russian";             MessagesFile: "compiler:Languages\Russian.isl"
Name: "german";              MessagesFile: "compiler:Languages\German.isl"
Name: "korean";              MessagesFile: "compiler:Languages\Korean.isl"
Name: "turkish";             MessagesFile: "compiler:Languages\Turkish.isl"
Name: "italian";             MessagesFile: "compiler:Languages\Italian.isl"
Name: "polish";              MessagesFile: "compiler:Languages\Polish.isl"
Name: "dutch";               MessagesFile: "compiler:Languages\Dutch.isl"
Name: "ukrainian";           MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
; 言語の接頭辞が無いものが既定（英語）。訳が無い言語はこれが出る。
ShellIntegration=Register the right-click menu and file associations
RegisteringShell=Registering the right-click menu and file associations...
RegisterFailed=The right-click menu and file associations could not be registered.%nYou can register them later from smarcivaZIP's settings.
DeleteSettings=Also delete smarcivaZIP's settings?%n%nIf you keep them, your current settings will be back when you reinstall.%n%n%1

japanese.ShellIntegration=右クリックメニューと関連付けを登録する
japanese.RegisteringShell=右クリックメニューと関連付けを登録しています...
japanese.RegisterFailed=右クリックメニューと関連付けを登録できませんでした。%nあとで smarcivaZIP の設定画面から登録できます。
japanese.DeleteSettings=smarcivaZIP の設定も削除しますか？%n%n残しておくと、入れ直したときに今の設定のまま使えます。%n%n%1

spanish.ShellIntegration=Registrar el menú contextual y las asociaciones de archivos
spanish.RegisteringShell=Registrando el menú contextual y las asociaciones de archivos...
spanish.RegisterFailed=No se pudieron registrar el menú contextual y las asociaciones de archivos.%nPuede hacerlo más tarde desde la configuración de smarcivaZIP.
spanish.DeleteSettings=¿Eliminar también la configuración de smarcivaZIP?%n%nSi la conserva, al reinstalar recuperará la configuración actual.%n%n%1

arabic.ShellIntegration=تسجيل قائمة السياق واقترانات الملفات
arabic.RegisteringShell=جارٍ تسجيل قائمة السياق واقترانات الملفات...
arabic.RegisterFailed=تعذّر تسجيل قائمة السياق واقترانات الملفات.%nيمكنك تسجيلها لاحقًا من إعدادات smarcivaZIP.
arabic.DeleteSettings=هل تريد حذف إعدادات smarcivaZIP أيضًا؟%n%nإن أبقيتها، ستعود إعداداتك الحالية كما هي عند إعادة التثبيت.%n%n%1

brazilianportuguese.ShellIntegration=Registrar o menu de contexto e as associações de arquivos
brazilianportuguese.RegisteringShell=Registrando o menu de contexto e as associações de arquivos...
brazilianportuguese.RegisterFailed=Não foi possível registrar o menu de contexto e as associações de arquivos.%nVocê pode registrá-los depois nas configurações do smarcivaZIP.
brazilianportuguese.DeleteSettings=Excluir também as configurações do smarcivaZIP?%n%nSe você as mantiver, suas configurações atuais voltarão ao reinstalar.%n%n%1

french.ShellIntegration=Enregistrer le menu contextuel et les associations de fichiers
french.RegisteringShell=Enregistrement du menu contextuel et des associations de fichiers...
french.RegisterFailed=Le menu contextuel et les associations de fichiers n'ont pas pu être enregistrés.%nVous pourrez le faire plus tard depuis les réglages de smarcivaZIP.
french.DeleteSettings=Supprimer aussi les réglages de smarcivaZIP ?%n%nSi vous les conservez, vous retrouverez vos réglages actuels en cas de réinstallation.%n%n%1

russian.ShellIntegration=Зарегистрировать контекстное меню и связи с файлами
russian.RegisteringShell=Регистрация контекстного меню и связей с файлами...
russian.RegisterFailed=Не удалось зарегистрировать контекстное меню и связи с файлами.%nЭто можно сделать позже в настройках smarcivaZIP.
russian.DeleteSettings=Удалить также настройки smarcivaZIP?%n%nЕсли их оставить, при повторной установке вернутся ваши текущие настройки.%n%n%1

german.ShellIntegration=Kontextmenü und Dateiverknüpfungen eintragen
german.RegisteringShell=Kontextmenü und Dateiverknüpfungen werden eingetragen ...
german.RegisterFailed=Kontextmenü und Dateiverknüpfungen konnten nicht eingetragen werden.%nSie können das später in den Einstellungen von smarcivaZIP nachholen.
german.DeleteSettings=Auch die Einstellungen von smarcivaZIP löschen?%n%nWenn Sie sie behalten, sind nach einer Neuinstallation wieder Ihre jetzigen Einstellungen da.%n%n%1

korean.ShellIntegration=오른쪽 클릭 메뉴와 파일 연결 등록
korean.RegisteringShell=오른쪽 클릭 메뉴와 파일 연결을 등록하는 중...
korean.RegisterFailed=오른쪽 클릭 메뉴와 파일 연결을 등록하지 못했습니다.%n나중에 smarcivaZIP 설정 화면에서 등록할 수 있습니다.
korean.DeleteSettings=smarcivaZIP 설정도 삭제할까요?%n%n남겨 두면 다시 설치했을 때 지금 설정 그대로 쓸 수 있습니다.%n%n%1

turkish.ShellIntegration=Bağlam menüsünü ve dosya ilişkilendirmelerini kaydet
turkish.RegisteringShell=Bağlam menüsü ve dosya ilişkilendirmeleri kaydediliyor...
turkish.RegisterFailed=Bağlam menüsü ve dosya ilişkilendirmeleri kaydedilemedi.%nBunu daha sonra smarcivaZIP ayarlarından yapabilirsiniz.
turkish.DeleteSettings=smarcivaZIP ayarları da silinsin mi?%n%nAyarları tutarsanız, yeniden kurduğunuzda şimdiki ayarlarınız geri gelir.%n%n%1

italian.ShellIntegration=Registra il menu contestuale e le associazioni dei file
italian.RegisteringShell=Registrazione del menu contestuale e delle associazioni dei file...
italian.RegisterFailed=Non è stato possibile registrare il menu contestuale e le associazioni dei file.%nPotrai farlo più tardi dalle impostazioni di smarcivaZIP.
italian.DeleteSettings=Eliminare anche le impostazioni di smarcivaZIP?%n%nSe le conservi, reinstallando ritroverai le impostazioni attuali.%n%n%1

polish.ShellIntegration=Zarejestruj menu podręczne i skojarzenia plików
polish.RegisteringShell=Rejestrowanie menu podręcznego i skojarzeń plików...
polish.RegisterFailed=Nie udało się zarejestrować menu podręcznego i skojarzeń plików.%nMożesz to zrobić później w ustawieniach smarcivaZIP.
polish.DeleteSettings=Usunąć także ustawienia smarcivaZIP?%n%nJeśli je zachowasz, po ponownej instalacji wrócą Twoje obecne ustawienia.%n%n%1

dutch.ShellIntegration=Contextmenu en bestandskoppelingen aanmelden
dutch.RegisteringShell=Contextmenu en bestandskoppelingen worden aangemeld...
dutch.RegisterFailed=Het contextmenu en de bestandskoppelingen konden niet worden aangemeld.%nJe kunt dat later doen via de instellingen van smarcivaZIP.
dutch.DeleteSettings=Ook de instellingen van smarcivaZIP verwijderen?%n%nAls je ze bewaart, krijg je bij een nieuwe installatie je huidige instellingen terug.%n%n%1

ukrainian.ShellIntegration=Зареєструвати контекстне меню та зв'язки з файлами
ukrainian.RegisteringShell=Реєстрація контекстного меню та зв'язків з файлами...
ukrainian.RegisterFailed=Не вдалося зареєструвати контекстне меню та зв'язки з файлами.%nЦе можна зробити пізніше в налаштуваннях smarcivaZIP.
ukrainian.DeleteSettings=Видалити також налаштування smarcivaZIP?%n%nЯкщо залишити їх, після повторного встановлення повернуться ваші поточні налаштування.%n%n%1

[Tasks]
Name: "shellintegration"; Description: "{cm:ShellIntegration}"
; 既定でオン。外せるようにはしておく（勝手に置くと嫌がる人もいるので、選べることは残す）。
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\7z.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Icons\*"; DestDir: "{app}\Icons"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\7-Zip-License.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 引数なしで起動すると設定画面が開く。スタートメニューからはそこに行ければ足りる。
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; ファイルを消す前に呼ぶ（[UninstallRun] は削除より先に走る）。
; 本体が無くなってからでは、登録を消す手段が無くなる。
Filename: "{app}\{#AppExe}"; Parameters: "--unregister --quiet"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterShell"

[Code]
{ 登録はファイルを置き終えてから行う。レジストリに書くのは実行ファイルとアイコンの
  パスなので、それらが揃う前に書くと、途中で失敗したときに行き先の無い登録が残る。
  失敗は終了コードで判定する。[Run] 節では終了コードを見られないため、ここで呼ぶ。 }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('shellintegration') then Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('RegisteringShell');

  { 管理者として起動されていても、登録は元の利用者の HKCU に書く必要がある。
    Exec では昇格した側（別の管理者アカウントのこともある）の HKCU に書かれてしまう。 }
  if not ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--register --quiet', '',
                            SW_HIDE, ewWaitUntilTerminated, ResultCode)
     or (ResultCode <> 0) then
  begin
    Log(Format('Shell registration failed (exit code %d).', [ResultCode]));
    SuppressibleMsgBox(CustomMessage('RegisterFailed'), mbError, MB_OK, IDOK);
  end;
end;

{ 設定を残すかどうかは本人に聞く。入れ直すつもりで消す人もいるので、既定は「残す」。
  サイレントアンインストールでは聞けないので、残す側に倒す。
  ログは診断用で、本体が無くなれば役目が無いので、どちらでも消す。 }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  SettingsDir := ExpandConstant('{userappdata}\{#AppName}');
  if not DirExists(SettingsDir) then Exit;

  if not UninstallSilent and
     (MsgBox(FmtMessage(CustomMessage('DeleteSettings'), [SettingsDir]),
             mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
  begin
    DelTree(SettingsDir, True, True, True);
  end
  else
  begin
    DeleteFile(SettingsDir + '\smarcivazip.log');
    { 更新確認の記録は設定ではないので、残す理由が無い。 }
    DeleteFile(SettingsDir + '\update.json');
    { 中身が空になったときだけ消える。設定が残っていれば何もしない。 }
    RemoveDir(SettingsDir);
  end;
end;
