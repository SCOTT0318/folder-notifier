# 폴더 알림

Windows 11에서 지정한 폴더에 새 파일이 추가되면 작업 표시줄 알림을 표시하는 데스크톱 앱입니다.

## 실행 방법

1. `dist/FolderNotifier-Setup.exe`를 실행하여 설치 (.NET 별도 설치 불필요)
2. 설치 완료 화면 또는 시작 메뉴의 **폴더 알림** 실행
3. **폴더 선택**으로 감시할 폴더 지정 (공유 폴더의 UNC 경로 직접 입력 가능)
4. 필요하면 **하위 폴더 포함** 선택
5. **감시 시작** 클릭
6. **알림 테스트**로 Windows 알림 표시 확인

설치 파일 하나에 앱과 런타임이 포함되어 있어 ZIP 압축 해제나 별도 런타임 설치가 필요 없습니다. 현재 사용자에게 설치되며, Windows **설정 → 앱 → 설치된 앱 → 폴더 알림**에서 제거합니다. 설정 파일은 업데이트 / 제거 시 유지합니다. 앱이 실행 중인 상태에서 업데이트하려면 트레이 메뉴의 **종료**로 먼저 종료하세요.

설치하지 않고 사용할 때는 `dist/FolderNotifier-1.2.0/FolderNotifier.exe`를 실행합니다.

**닫기(X)** 또는 **트레이로 숨기기**를 누르면 트레이에서 계속 실행됩니다. 트레이 아이콘을 두 번 클릭하면 창이 열립니다. 완전히 끝내려면 **앱 종료** 또는 트레이 메뉴의 **종료**를 사용하세요.

**Windows 로그인 시 자동 실행**을 켜면 다음 Windows 로그인부터 트레이에서 실행하고 저장된 폴더 감시를 시작합니다. 이 옵션은 변경 즉시 적용되며, 끄면 자동 실행 등록을 삭제합니다. 저장된 폴더가 없으면 로그인 시 폴더를 선택할 수 있도록 창을 표시합니다.

**앱 실행 시 감시 시작**은 사용자가 앱을 직접 실행했을 때 감시를 시작하는 별도 옵션입니다.

Windows 자동 실행은 현재 사용자 `HKCU/Software/Microsoft/Windows/CurrentVersion/Run`의 `FolderNotifier` 값에 실행 파일 경로와 `--startup`을 등록합니다. 앱 제거 시 해당 설치 경로를 가리키는 등록도 삭제합니다. 폴더 선택 등 사용자 설정은 유지합니다. Windows 시작 앱 설정에서 별도로 사용 안 함으로 지정한 경우에는 해당 설정에서도 다시 켜야 합니다.

## 동작 범위

- 약 2초 간격으로 전체 파일 목록을 비교합니다. 큰 폴더와 네트워크 상태에 따라 더 오래 걸릴 수 있습니다.
- 감시 시작 후 최초로 성공한 조회의 기존 파일은 알림 대상에서 제외합니다.
- 파일 내용 수정과 삭제는 알리지 않습니다. 이름 변경은 새로운 경로로 간주하여 알립니다.
- 한 번에 여러 파일이 추가되면 알림 하나로 묶고 앱 이력에 파일별로 표시합니다.
- 하위 폴더의 숨김 파일을 포함하여 감시하며, 연결 지점과 심볼릭 링크는 순환 감시 방지를 위해 제외합니다.
- 공유 폴더의 연결 / 권한 오류는 화면과 이력에 표시하고 자동 재시도합니다. 실패한 조회는 기존 파일 목록을 덮어쓰지 않습니다.
- 조회 사이에 생겼다가 사라지는 파일은 감지하지 못할 수 있습니다. 알림은 파일 경로가 발견되었다는 의미이며 복사 완료를 보장하지 않습니다.
- 중지 중이거나 앱이 종료된 동안의 추가 파일은 다음 감시 시작 시 기존 파일로 처리합니다.
- 이력은 메모리에 최근 500행만 유지하며 앱 종료 시 사라집니다.
- 폴더와 옵션은 `%LOCALAPPDATA%/FolderNotifier/settings.json`에 저장합니다. 감시 대상 파일은 수정하지 않습니다.

알림이 보이지 않으면 Windows **설정 → 시스템 → 알림**과 **방해 금지** 상태를 확인하세요. **알림 테스트**는 알림 표시 요청만 수행하며, 실제 배너 표시 여부는 Windows 설정에 따라 달라집니다.

## 개발 및 빌드

.NET 10 SDK와 Windows가 필요합니다. C# WPF와 .NET 내장 Fluent 테마 기반이며 트레이 알림에는 Windows Forms NotifyIcon을 사용합니다. 외부 NuGet 라이브러리와 Python / Conda 의존성은 없습니다. 창은 작업영역에 맞춰 열리고, 작은 화면에서는 스크롤로 모든 조작부에 접근할 수 있습니다.

```powershell
dotnet run --project FolderNotifier.Tests -c Release -- .
dotnet build FolderNotifier.UiTests -c Release -m:1 -nr:false
dotnet run --project FolderNotifier.UiTests -c Release --no-build -- .
.\build.ps1
```

기본 빌드 결과는 **.NET 10 Desktop Runtime (x64)**이 설치된 PC에서 실행합니다. 별도 .NET 설치 없이 배포하려면 다음 명령으로 자체 포함 빌드하세요. 최초 빌드 때 Microsoft 런타임 패키지 다운로드가 필요할 수 있습니다.

```powershell
.\build.ps1 -SelfContained
```

배포할 때는 `dist/FolderNotifier` 폴더 전체를 함께 전달하세요. 자체 포함 빌드는 `dist/FolderNotifier-1.2.0`처럼 앱 버전을 포함한 폴더에 생성되며 해당 폴더 전체를 전달합니다.

일반 빌드를 사용한다면 `dist/FolderNotifier/FolderNotifier.exe`를 실행합니다.

설치 EXE를 생성하려면 다음 명령을 사용합니다. 최초 실행 시 공식 Inno Setup portable 컴파일러를 프로젝트 안에 준비하고 SHA-256 및 게시자 서명을 확인합니다.

```powershell
.\build-installer.ps1
.\installer\verify-installer.ps1
```

생성 결과는 `dist/FolderNotifier-Setup.exe`입니다. 설치기는 한국어 UI, 시작 메뉴, 선택적 바탕 화면 바로가기, 제거 기능과 설치 완료 후 실행을 지원합니다. 앱 / 설치기는 코드 서명 인증서가 없는 빌드이므로 Windows에서 게시자 확인 경고가 표시될 수 있습니다.

## 앱 아이콘

파란 폴더와 노란 알림 배지를 조합한 자체 벡터 아이콘을 실행 파일, 창, 트레이에 적용합니다. ICO에는 16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256px 이미지가 포함됩니다.

원본과 미리보기는 `FolderNotifier/Assets/`의 SVG / PNG / ICO 파일입니다. 아이콘을 다시 생성하려면 다음 명령을 실행한 뒤 앱을 빌드합니다.

```powershell
dotnet build tools/IconBuilder -c Release -m:1 -nr:false
dotnet run --project tools/IconBuilder -c Release --no-build -- FolderNotifier/Assets
```

기반 API 문서: [Windows Forms NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon)

화면 테마 참고: [Microsoft WPF Fluent 테마](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90), [Windows 11 geometry](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry)
