# 폴더 알림 · Folder Notifier

Windows 11에서 **새 파일이 들어오면 알려주는 폴더 감시 앱**입니다.
폴더를 선택하고 감시를 시작하면 Windows 알림과 앱의 활동 이력으로 새 파일을 확인할 수 있습니다.

## 다운로드

### [⬇ Windows 설치 EXE 다운로드](https://github.com/SCOTT0318/folder-notifier/releases/latest/download/FolderNotifier-Setup.exe)

**Windows 11 · x64 · 한국어 · 약 52 MiB**

설치 EXE 하나에 앱과 .NET 실행 구성요소가 포함되어 있습니다. 별도 .NET 설치나 ZIP 압축 해제가 필요하지 않습니다.

[최신 릴리스 및 변경 내용](https://github.com/SCOTT0318/folder-notifier/releases/latest) · [SHA-256 확인 파일](https://github.com/SCOTT0318/folder-notifier/releases/latest/download/FolderNotifier-Setup.sha256)

> 현재 배포본에는 코드 서명을 적용하지 않아 Windows에서 게시자 확인 경고가 표시될 수 있습니다.

## 화면

![폴더 알림 화면 — Windows 로그인 시 자동 실행 설정 포함](docs/images/app.png)

## 주요 기능

- **새 파일 알림**: 약 2초마다 폴더를 확인하고 추가된 파일을 알립니다.
- **하위 폴더 감시**: 선택한 폴더 안의 폴더까지 함께 확인합니다.
- **로그인 자동 실행**: 체크하면 다음 Windows 로그인부터 트레이에서 저장된 폴더를 감시합니다.
- **트레이 실행**: 창을 닫아도 계속 감시하고, 트레이 아이콘으로 창을 다시 열 수 있습니다.
- **최근 활동**: 파일별 이력과 감시 상태를 최근 500개까지 표시합니다.
- **연결 복구**: 네트워크 공유 폴더의 연결 / 접근 오류를 표시하고 자동 재시도합니다.
- **Windows 11 스타일 화면**: 둥근 카드, 토글 스위치, 상태 표시와 작은 화면 스크롤을 지원합니다.

## 설치와 사용

1. 위의 **Windows 설치 EXE 다운로드**를 눌러 설치 파일을 받습니다.
2. `FolderNotifier-Setup.exe`를 실행하고 설치합니다.
3. 설치 완료 화면 또는 시작 메뉴에서 **폴더 알림**을 엽니다.
4. **폴더 선택**으로 감시할 폴더를 지정합니다. 공유 폴더의 UNC 경로도 입력할 수 있습니다.
5. **감시 시작**을 누릅니다.
6. **알림 테스트**로 알림 표시를 확인합니다.

설치는 현재 사용자에게 적용되며, 관리자 권한을 요청하지 않습니다. 기본 위치는 `%LOCALAPPDATA%\Programs\FolderNotifier`입니다. 바탕 화면 바로가기는 설치 중 선택할 수 있습니다.

### 컴퓨터를 켤 때 자동 실행

오른쪽 **감시 설정 → Windows 로그인 시 자동 실행**을 켜세요.

- 변경 즉시 저장되며, 다음 로그인부터 창을 띄우지 않고 트레이에서 감시합니다.
- 체크를 해제하면 자동 실행 등록을 삭제합니다.
- 저장된 폴더가 없으면 로그인 시 폴더를 선택할 수 있도록 창을 표시합니다.
- **앱 실행 시 감시 시작**은 앱을 직접 열었을 때의 별도 옵션입니다.

Windows의 시작 앱 설정에서 별도로 사용 안 함으로 지정했다면 해당 설정에서도 다시 켜야 합니다.

### 숨기기와 종료

**닫기(X)**는 트레이로 숨깁니다. 트레이 아이콘을 두 번 클릭하면 창이 열립니다.
완전히 종료하려면 앱의 **앱 종료** 또는 트레이 메뉴의 **종료**를 선택하세요.

### 업데이트와 제거

업데이트 전에는 실행 중인 앱을 트레이에서 종료하고 새 설치 EXE를 실행하세요.
제거는 **Windows 설정 → 앱 → 설치된 앱 → 폴더 알림 → 제거**에서 진행합니다.

폴더와 옵션은 `%LOCALAPPDATA%\FolderNotifier\settings.json`에 저장되며 업데이트 / 제거 시 유지합니다. 제거할 때 해당 설치 경로의 Windows 자동 실행 등록은 삭제합니다.

## 동작 기준

- 최초 감시 시작 시 이미 있던 파일은 알리지 않습니다.
- 파일 내용 수정과 삭제는 알리지 않으며, 이름 변경은 새로운 경로로 간주합니다.
- 여러 파일이 함께 추가되면 알림 하나로 묶고 파일별 이력을 표시합니다.
- 알림은 파일 경로 발견을 의미하며 복사 완료를 보장하지 않습니다.
- 조회 사이에 생겼다가 사라지는 파일은 놓칠 수 있습니다. 큰 폴더나 느린 네트워크에서는 확인 간격이 길어질 수 있습니다.
- 앱이 꺼졌거나 감시가 중지된 동안의 파일은 다음 감시 시작 시 기존 파일로 처리합니다.
- 연결 지점과 심볼릭 링크는 제외합니다. 활동 이력은 앱 종료 시 사라집니다.
- 감시 대상 파일은 수정하지 않습니다.

알림이 보이지 않으면 **Windows 설정 → 시스템 → 알림**과 **방해 금지**를 확인하세요.

## 개발과 빌드

Windows와 **.NET 10 SDK**가 필요합니다. C# WPF / .NET 내장 Fluent 테마를 사용하며, 트레이 알림에는 Windows Forms `NotifyIcon`을 사용합니다.

저장소 루트에서 PowerShell로 실행합니다.

```powershell
git clone https://github.com/SCOTT0318/folder-notifier.git
cd folder-notifier

# 파일 감시 검증
dotnet run --project FolderNotifier.Tests -c Release -- .

# 화면, 자동 실행 등록 / 해제, 트레이 감시 검증
dotnet build FolderNotifier.UiTests -c Release -m:1 -nr:false
dotnet run --project FolderNotifier.UiTests -c Release --no-build -- .

# .NET 런타임을 포함한 앱 빌드
.\build.ps1 -SelfContained

# 설치 EXE 빌드
.\build-installer.ps1

# 격리 경로에서 설치 / 재설치 / 제거 및 파일 해시 검증
.\installer\verify-installer.ps1
```

설치기 빌드 시 공식 Inno Setup portable 컴파일러를 프로젝트 내부에 준비하고 다운로드의 SHA-256 / 게시자 서명을 확인합니다. 최초 빌드에는 네트워크 연결이 필요합니다. 동기화 폴더에서 설치 파일이 손상되는 것을 방지하도록 로컬 임시 폴더에서 생성한 뒤 해시를 확인해 복사합니다.

자동 실행 테스트는 전용 테스트 레지스트리 키와 별도 설정 파일을 사용합니다. 설치 검증은 격리 경로에서 진행하며 실제 시작 프로그램 / 바로가기 / 설치 앱 등록과 Windows 재부팅은 테스트에 포함하지 않습니다.

빌드 결과는 `dist/`에 생성되며 Git에 포함하지 않습니다. 배포 EXE는 [GitHub Releases](https://github.com/SCOTT0318/folder-notifier/releases)에서 제공합니다.

[개발 상세](docs/DEVELOPMENT.md) · [설치기 빌드 상세](installer/README.md)

## 프로젝트 구성

| 경로 | 내용 |
| --- | --- |
| `FolderNotifier/` | WPF 화면, 트레이 알림, 설정, Windows 자동 실행 |
| `FolderNotifier.Core/` | 폴더 파일 목록 비교와 오류 복구 |
| `FolderNotifier.Tests/` | 파일 감시 검증 |
| `FolderNotifier.UiTests/` | 화면 및 자동 실행 통합 검증 |
| `installer/` | 설치 / 제거 스크립트와 검증 |
| `tools/IconBuilder/` | SVG / PNG / 여러 크기 ICO 생성 |
| `docs/` | 화면 미리보기와 개발 문서 |
