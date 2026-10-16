# 폴더 알림 설치 파일

`dist\FolderNotifier-Setup.exe` 하나를 받아 실행하면 설치할 수 있습니다. 별도 .NET 설치나 ZIP 압축 해제가 필요하지 않습니다.

- Windows 11 x64 대상, 현재 사용자 전용 설치, 관리자 권한 요청 없음
- 기본 설치 위치: `%LOCALAPPDATA%\Programs\FolderNotifier`
- 한국어 설치 화면, Windows 밝음/어두움 설정을 따르는 Windows 11 설치 스타일
- 시작 메뉴 바로가기, 선택적 바탕 화면 바로가기, 설치 후 앱 실행
- 앱 설정의 **로그인 시 자동 실행** 옵션으로 로그인 후 트레이 감시 시작 / 해제
- 제거 시 해당 설치 경로를 가리키는 자동 실행 등록 삭제
- Windows **설정 → 앱 → 설치된 앱 → 폴더 알림 → 제거** 지원
- `%LOCALAPPDATA%\FolderNotifier\settings.json`의 사용자 설정은 업데이트/제거 중 유지
- 앱 실행 중 업데이트/제거는 트레이 메뉴의 **종료**를 안내하며, 앱을 강제로 종료하지 않음

## 다시 빌드

```powershell
.\build-installer.ps1
```

최초 실행은 서명과 SHA-256을 검증한 공식 Inno Setup 7.1.0 컴파일러를 `tools\InnoSetup\compiler`에 portable 모드로 준비합니다. 시스템 Inno Setup 설치나 파일 연결 변경은 수행하지 않습니다. 다운로드에는 네트워크 연결이 필요합니다. 이미 앱을 자체 포함 빌드했다면 `-SkipPublish`를 사용할 수 있습니다.

설치 EXE는 동기화 폴더에 직접 컴파일할 때의 압축 파일 손상을 피하도록 로컬 임시 폴더에서 생성한 뒤 `dist`로 복사합니다. 복사 전후 SHA-256이 같음을 확인하고 생성한 임시 파일만 정리합니다.
제한된 실행 환경에서 기본 임시 폴더의 실행 파일 리소스 수정이 허용되지 않으면 `-StagingRoot`에 쓰기 가능한 로컬 임시 폴더를 지정할 수 있습니다.

```powershell
.\build-installer.ps1 -SkipPublish -AppVersion 1.6.0
.\installer\verify-installer.ps1
```

버전별 별도 배포 폴더를 지정하려면 두 명령에 같은 `-PublishDirectory`를 전달합니다.

```powershell
.\build-installer.ps1 -SkipPublish -PublishDirectory 'dist\FolderNotifier-1.6.0'
.\installer\verify-installer.ps1 -PublishDirectory 'dist\FolderNotifier-1.6.0'
```

검증 스크립트는 같은 설치 EXE를 작업 폴더 아래 격리된 경로로 설치하고 파일 해시/제거를 검사합니다. `/SMOKETEST=1` 옵션으로 시작 메뉴/바탕 화면 바로가기, 설치 앱 등록, 별도 실행 중인 사용자 앱의 mutex 검사를 생략하며, 앱을 실행/종료하거나 실제 사용자 설정을 변경하지 않습니다. 일반 설치/제거에는 실행 중인 앱 확인이 적용됩니다.

공식 자료: [Inno Setup 다운로드](https://jrsoftware.org/isdl.php), [portable 모드 원본](https://github.com/jrsoftware/issrc/blob/main/isportable.iss), [Windows 11 설치 스타일](https://jrsoftware.org/ishelp/topic_setup_wizardstyle.htm).

이 프로젝트 산출물에는 코드 서명 인증서를 적용하지 않았습니다.
