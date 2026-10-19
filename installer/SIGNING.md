# 코드 서명 배포 준비

현재 공개 v1.2.0 설치 파일은 **서명되지 않았습니다**. 아래 절차는 발급 완료 후 사용할 빌드 지원이며, 인증서를 발급하거나 SmartScreen 경고 제거를 보장하지 않습니다.

## 발급 선행 조건

공개 배포용 인증 기관 또는 Public Trust 서명 서비스에서 발급 명의 확인을 완료해야 합니다. 직접 만든 인증서나 Private Trust 프로필은 공개 배포용으로 사용하지 않습니다. 결제와 신원 확인은 명의자가 공식 서비스에서 진행합니다. 신분증, 개인 키, 토큰, 비밀번호를 저장소나 이슈에 올리지 마세요.

- [Microsoft Artifact Signing 발급 조건](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart): 한국 조직 지원, 개인은 미국·캐나다만 지원. Azure 구독과 신원 확인 필요.
- [SignPath Foundation](https://signpath.org/): 자격 요건을 충족하는 오픈소스 프로젝트 대상 무료 서비스. 공개 GitHub 저장소만으로 승인이 보장되지 않습니다. 프로젝트 라이선스와 신청 승인이 먼저 필요합니다.

## 무료 경로 확인 결과 (2026-10-16)

GitHub에서 설치 EXE를 직접 제공하는 현재 방식에는 **SignPath Foundation의 무료 오픈소스 서명**을 신청하는 경로가 있습니다. 개인 명의 인증서를 무료로 발급해 주는 방식이 아니라 재단 명의 인증서로 승인된 프로젝트를 서명하는 서비스입니다. 선정은 재단 심사에 따르며, 실행 프로그램은 검증 가능한 프로젝트 평판도 요구합니다. 새 프로젝트의 승인과 처리 기간을 보장할 수 없습니다.

| 공식 신청 조건 | 현재 프로젝트에서 확인한 상태 |
| --- | --- |
| OSI 승인 오픈소스 라이선스, 상업적 이중 라이선스 없음 | LICENSE 파일 없음. 저작권자의 라이선스 선택 필요 |
| 유지 관리, 기존 배포, 기능 및 제거 방법 문서 | 소스와 v1.2.0 EXE 배포 및 README 있음 |
| 소스로부터 검증 가능한 자동 빌드 | 로컬 빌드 스크립트 있음. GitHub Actions / SignPath 연동은 아직 없음 |
| GitHub와 SignPath 계정 다중 인증 | 사용자 확인 필요. 이 점검에서는 계정 설정을 변경하지 않음 |
| Code signing policy, 역할, 개인정보 처리 안내 | 승인 조건에 맞춘 정책 작성과 서명 승인자 지정 필요 |
| 모든 서명 요청의 수동 승인 | 실제 서비스 가입 및 프로젝트 승인 후 설정 필요 |

서비스 제공 표시는 승인 후 게시합니다. 아직 무료 서명 승인을 받은 것처럼 표기하거나, 저작권자 동의 없이 MIT 등의 라이선스를 적용하거나, 외부 신청을 보내지 않았습니다. 기존의 로컬 서명 어댑터만으로 SignPath의 빌드 출처 검증이 완료되는 것은 아니며 해당 서비스의 CI 연동을 별도로 구성해야 합니다.

대안으로 Microsoft Store에 **MSIX** 패키지를 제출하면 심사 후 Microsoft가 패키지를 무료로 서명합니다. 이 혜택은 Store 배포용이며, 현재 GitHub의 설치 EXE를 Microsoft가 서명해 주는 것은 아닙니다. Store의 MSI / EXE 제출과 Store 외부 배포에는 별도 서명이 필요합니다.

근거: [SignPath 신청 조건과 프로젝트 평판 요건](https://signpath.org/terms), [SignPath GitHub 빌드 출처 검증](https://docs.signpath.io/trusted-build-systems/github), [Microsoft Store 서명 범위](https://learn.microsoft.com/en-us/windows/apps/publish/get-started).

## 발급 후 빌드

승인된 서비스에 맞는 로컬 `signing.local.ps1` 어댑터를 준비합니다. 이 파일은 `param([string]$Path)`로 파일 하나를 받아 SHA-256 Authenticode 서명과 신뢰된 타임스탬프를 적용하고, 오류 시 `throw`해야 합니다. 자격 증명은 서비스의 안전한 저장소나 하드웨어 토큰에서 사용합니다. 어댑터는 Inno Setup이 실행하는 Windows PowerShell 5.1에서도 동작해야 합니다.

```powershell
# 실제 발급 인증서의 Subject 전체를 정확히 지정합니다.
$publisher = 'CN=실제 발급 명의, O=실제 조직, C=KR'
.\build-installer.ps1 -SigningScript .\signing.local.ps1 -ExpectedPublisher $publisher
.\installer\verify-installer.ps1 -RequireSigned -ExpectedPublisher $publisher
```

앱 EXE와 자체 DLL 두 개를 먼저 서명하고 Inno Setup에서 설치 EXE와 제거 프로그램을 서명합니다. 각 서명은 Windows 검증 상태 `Valid`, 정확한 발급 명의, 타임스탬프를 확인하며 자체 서명 인증서는 거부합니다. 예상 명의는 서비스 발급 기록과 대조해야 하며 로컬 신뢰 저장소 검증만으로 공개 신뢰 자격을 증명하지는 않습니다. Microsoft 런타임 파일은 다시 서명하지 않습니다.

서명 실패 시 새 설치 파일을 `dist/FolderNotifier-Setup.exe`로 복사하지 않습니다. SHA-256은 최종 서명이 적용된 설치 파일에서 계산합니다. 서명 후 설치·재설치·제거 검증이 완료된 파일과 체크섬만 새 GitHub 릴리스에 업로드합니다. 현재 인증서가 없어 실제 서명 성공 경로는 아직 검증하지 못했습니다.

서명 어댑터를 지정하지 않은 기존 빌드 명령은 개발용 미서명 설치 파일을 생성합니다. 공개 서명 릴리스에는 반드시 `-RequireSigned` 검증을 적용하세요.

[Inno Setup SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm) · [서명된 제거 프로그램](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
