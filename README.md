# PolarAd (MVP)

Windows 10/11용 **브라우저 공통 광고·추적 차단** 프로그램. Chrome, Edge, Firefox에
확장 프로그램을 설치하지 않고, 프로그램 하나로 OS 레벨에서 광고/추적 도메인을
차단합니다.

> 이 저장소는 독립적으로 설계·구현한 결과물입니다. 유니콘 Pro나 다른 제품의
> 코드·에셋·브랜드를 복제하지 않았습니다.

---

## 1. 전체 구조

```
PolarAd/
  src/
    PolarAd.Core/     DNS 프록시, 규칙 엔진, 차단목록 갱신, 로그, 네트워크 설정 — UI 없는 핵심 엔진
    PolarAd.App/      WPF 트레이 앱 (대시보드 / 규칙 관리 / 차단 기록 / 설정)
  tests/
    PolarAd.Tests/    xUnit 테스트 (규칙 엔진, hosts 파서, 실제 소켓 기반 DNS 프록시 E2E 테스트)
  scripts/
    restore-dns.ps1    앱 없이도 실행 가능한 긴급 DNS 복구 스크립트
  publish/             `dotnet publish`로 만든 배포용 단일 exe (최초 빌드 후 생성됨)
```

### 기술 스택 선택 이유
- **.NET 8 / C# (WPF)** — 이 PC에는 Node, CMake, Visual Studio 네이티브 툴체인이
  없었고(.NET SDK도 winget으로 새로 설치함), .NET만 추가 설치 한 번으로 바로
  빌드·실행이 가능했습니다. WinForms(`NotifyIcon`)를 트레이 아이콘에 섞어 썼습니다.

### 차단 방식: DNS 싱크홀을 1차 수단으로 선택 (WinDivert/WFP 검토 결과)
- **선택한 방식**: 로컬 PC에 127.0.0.1:53로 동작하는 자체 DNS 서버를 띄우고,
  OS의 활성 네트워크 어댑터 DNS 서버를 이 서버로 돌립니다. Chrome/Edge/Firefox는
  별도 설정 없이 OS DNS를 그대로 쓰므로, **브라우저별 확장 설치 없이 3개 브라우저
  모두에 동시 적용**됩니다. 차단 목록에 있는 도메인은 NXDOMAIN으로 응답하고,
  나머지는 업스트림(기본 1.1.1.1)으로 그대로 포워딩합니다.
- **WinDivert 검토**: 패킷/소켓 레벨에서 가로채는 커널 드라이버로, TLS
  ClientHello의 SNI를 보고 도메인 단위로 차단하거나 DNS-over-HTTPS를 강제로
  우회시키는 데 쓸 수 있습니다. 다만 (1) 서명된 커널 드라이버를 배포·로드해야
  해서 보안 소프트웨어/일부 환경과 충돌 가능성이 있고, (2) 세션 하나짜리 MVP에서
  드라이버 설치·언로드·크래시 복구까지 검증하기엔 리스크가 컸습니다. 공개
  차단목록 기반 차단은 DNS 싱크홀만으로도 대부분 커버되므로, 이번 MVP에는
  포함하지 않았습니다.
- **WFP(Windows Filtering Platform) 검토**: WinDivert보다 "정식" API이고
  드라이버 서명 문제가 적지만, 콜아웃 드라이버 등록·관리가 WinDivert보다 복잡하고
  문서화된 예제가 적어 MVP 타임라인에는 맞지 않다고 판단했습니다.
- **다음 단계 권장안**: Secure DNS(DoH)를 쓰는 브라우저가 우리 DNS 프록시를
  건너뛰는 경우를 막기 위해, WinDivert로 "우리 프록시가 아닌 곳으로 가는 UDP
  53번 포트"와 "알려진 DoH 서버의 443 SNI"를 차단하는 보완 레이어를 추가하는 것을
  권장합니다. (이번 MVP에는 미구현)

### HTTPS 내용 검사(MITM)는 이번 MVP에 **구현하지 않음**
- 요구사항대로 사용자의 명시적 조작 없이 루트 CA를 설치하거나 HTTPS를
  가로채는 코드는 전혀 포함하지 않았습니다.
- 향후 구현 시 설계: 로컬 HTTPS 프록시(예: Titanium Web Proxy 같은 MIT 라이선스
  라이브러리)를 **기본 OFF**인 별도 옵션으로 추가하고, 사용자가 명시적으로 켤 때만
  (1) 로컬 전용 루트 CA를 생성하고 (2) "설치" 버튼을 눌러야 Windows 인증서 저장소에
  등록되도록 합니다. 금융 앱, VPN, 인증서 고정(certificate pinning) 앱은 이 프록시를
  타지 않도록 프로세스/도메인 단위 예외 목록을 두어야 합니다. 이 부분은 설계만
  해두었고 동작하는 코드는 없습니다 — "구현했다"고 표시하지 않습니다.

---

## 2. 빌드 방법

사전 요구사항: Windows 10/11 x64, .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).

```bash
cd PolarAd
dotnet build PolarAd.sln -c Debug
```

배포용 단일 실행 파일 생성(약 150MB, .NET 런타임 포함 self-contained):

```bash
dotnet publish src/PolarAd.App/PolarAd.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## 3. 실행 방법

```bash
# 개발 중 바로 실행 (디버그 빌드)
dotnet run --project src/PolarAd.App/PolarAd.App.csproj
```

또는 `publish\PolarAd.exe`를 더블클릭.

**중요**: 프로그램은 실행 시 항상 **관리자 권한(UAC)을 요청**합니다. 이유:
- DNS 서버 변경에는 `Win32_NetworkAdapterConfiguration`에 대한 관리자 권한이 필요합니다.
- UDP 53번 포트를 리슨하는 DNS 프록시도 관리자 권한에서 더 안정적으로 동작합니다.

처음 창이 열리면:
1. **대시보드 → "보호 기능 켜기"** 를 누르면 DNS 프록시가 뜨고, 시스템 DNS가
   127.0.0.1로 바뀝니다(원래 값은 자동 백업됨).
2. **대시보드 → "지금 차단 목록 업데이트"** 를 눌러 공개 차단목록(StevenBlack
   hosts)을 내려받습니다. 인터넷 연결이 필요합니다. 한 번 받아두면 로컬 캐시
   파일(`%LocalAppData%\PolarAd\blocklist_cache.txt`)로 오프라인에서도 재사용됩니다.
   이후로는 **설정 탭에 지정한 주기(기본 24시간)마다 프로그램이 알아서 다시
   받아옵니다** — 트레이에 떠 있는 동안 백그라운드에서 계속 돕니다.
3. 창을 닫아도(X) 트레이로 숨겨질 뿐 보호는 계속 동작합니다. 완전히 끄려면
   트레이 아이콘 우클릭 → **"종료 (네트워크 설정 복구 후)"**.

### 설치 프로그램 (Inno Setup)

`dotnet run`/`publish` 대신 정식 설치 프로그램으로 배포하고 싶다면:

```bash
# 1) Release로 먼저 publish (설치 프로그램이 publish\ 폴더를 그대로 패키징함)
dotnet publish src/PolarAd.App/PolarAd.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# 2) Inno Setup으로 설치 프로그램 빌드 (winget install JRSoftware.InnoSetup)
cd installer
iscc PolarAd.iss
# 결과물: installer\output\PolarAdSetup.exe
```

`PolarAdSetup.exe`는 관리자 권한을 요구하고, `%ProgramFiles%\PolarAd`에 설치하며, 설치
중 다음을 선택할 수 있습니다:
- **"Windows 시작 시 자동 실행"** (기본 체크됨) — Windows **작업 스케줄러**에 "로그온 시,
  가장 높은 권한으로 실행" 작업을 등록합니다. 레지스트리 시작프로그램(Run 키) 대신
  작업 스케줄러를 쓴 이유: 이 프로그램은 관리자 권한이 필요한데, Run 키 방식은
  로그온할 때마다 UAC 창이 뜨지만 작업 스케줄러는 미리 "가장 높은 권한"으로
  등록해두면 조용히 자동 실행됩니다. 설치 후에도 **설정 탭의 체크박스**로 언제든
  켜고 끌 수 있습니다(`AutoStartManager.cs`).
- 바탕화면 바로가기 생성 (기본 선택 안 됨)

**제거 시 안전장치**: 설치 제거 프로그램은 (1) 실행 중인 PolarAd.exe를 종료하고,
(2) `restore-dns.ps1`을 무조건 실행해서 DNS를 자동(DHCP)으로 되돌리고, (3) 작업
스케줄러 등록을 지우는 순서로 동작합니다. 앱이 자기 스스로 정상 종료하지
못했더라도(강제 종료 등) DNS는 항상 복구되도록 설계했습니다.

## 4. 테스트 방법

```bash
dotnet test tests/PolarAd.Tests/PolarAd.Tests.csproj
```

포함된 테스트 (22개, 모두 통과 확인됨):
- `RuleEngineTests` — 공개 차단목록/사용자 규칙/허용목록의 우선순위와 서브도메인
  매칭 로직.
- `HostsFileParserTests` — hosts 포맷 파싱, 주석/로컬호스트 라인 무시, 한 줄에
  여러 도메인 처리.
- `AdblockRuleParserTests` — AdBlock Plus/uBlock 문법(`||domain^`, `@@||domain^`
  예외) 파싱. **`Rules_with_any_modifier_are_never_treated_as_a_full_domain_block`
  는 실사용 중 발견된 실제 버그의 회귀 테스트입니다** — 자세한 내용은 "9. 알려진
  한계" 하단의 "실사용 중 발견·수정한 문제" 참고.
- `DnsMessageUtilsTests` — DNS 쿼리에서 질의 도메인 추출, NXDOMAIN 응답 생성.
- `DnsProxyServerTests` — **실제 UDP 소켓**으로 비관리자 테스트 포트(2만번대)에
  `DnsProxyServer`를 띄워, 차단 도메인은 NXDOMAIN이 오는지, 허용 도메인은 실제
  업스트림(1.1.1.1)으로 포워딩되어 정상 응답이 오는지, 허용목록이 차단목록보다
  우선하는지를 end-to-end로 검증합니다. (이 테스트는 인터넷 연결이 필요합니다.)
  시스템의 실제 DNS 설정이나 53번 포트는 건드리지 않습니다.
- `NetworkConfiguratorTests` — WMI로 실제 네트워크 어댑터 목록을 읽어오는 부분만
  검증 (DNS를 실제로 바꾸는 부분은 자동 테스트하지 않음 — 9장 참고).

## 5. 실제로 검증한 것 / 검증하지 못한 것

자동화 테스트로 **직접 확인한 것**:
- `dotnet build` (Debug/Release, 3개 프로젝트) 성공, 경고 0 / 오류 0.
- `dotnet test` 22/22 통과, DNS 차단·포워딩·허용목록 우선순위를 실제 소켓으로 검증.
- `dotnet publish` self-contained 단일 exe 생성 성공.

그리고 **실제 사용자가 관리자 권한으로 직접 켜고 실제 브라우저에서 확인**한
결과 (아래 9장 "실사용 중 발견·수정한 문제" 참고, 사용한 브라우저는 8장 표에
기록):
- 보호 기능을 켜면 실제로 시스템 DNS가 127.0.0.1로 바뀌고, criteo/doubleclick/
  googletagmanager 등 글로벌 광고망과 AdGuard 한국어 필터 목록의 도메인들이
  실제로 차단되는 것을 로그로 확인.
- 디씨인사이드·네이버·루리웹에서 실제 테스트 중 발견한 버그 1건을 수정하고
  회귀 테스트로 고정.
- 네이버(`g.tivan.naver.com` 커스텀 규칙)와 루리웹(`image.ruliweb.com` 커스텀
  규칙) 둘 다 해당 광고가 사라지고 다른 기능은 정상 동작함을 확인.

**아직 확인 못 한 것**: Edge/Firefox에서의 직접 테스트, 유튜브 광고 차단(아래
7장 — 구조적으로 어려움).

## 6. 직접 해보셔야 하는 것 (브라우저별 테스트 절차)

1. `dotnet run --project src/PolarAd.App/PolarAd.App.csproj` 실행 → UAC 승인.
2. 대시보드에서 "지금 차단 목록 업데이트" → "보호 기능 켜기".
3. Chrome/Edge/Firefox에서 광고가 많은 일반 뉴스/커뮤니티 사이트 방문 → 배너
   광고/추적 스크립트가 줄어드는지 확인. (세 브라우저 모두 같은 OS DNS를 쓰므로
   동일하게 동작해야 합니다. 단, Firefox가 "DNS over HTTPS"를 자체적으로
   활성화해둔 경우에는 우리 DNS 프록시를 건너뛰어 차단이 적용되지 않을 수
   있습니다 — Firefox 설정 → 개인 정보 및 보안 → "DNS over HTTPS"를 끄거나
   "기본 보호"로 두세요.)
4. 사이트가 깨지면: **차단 기록** 탭에서 해당 도메인을 선택 → "선택 도메인 즉시
   허용" → 사이트 새로고침.
5. 유튜브에서 영상 재생 전/중간 광고가 차단되는지 확인 — **차단되지 않을
   가능성이 높습니다.** 아래 7번 참고.

## 7. 유튜브 광고 차단 — 별도 난도 높은 기능, MVP에서 검증되지 않음

유튜브는 광고를 콘텐츠와 **같은 도메인(googlevideo.com 등)**, 같은 CDN
스트림 안에 서버 측에서 끼워 넣는(Server-Side Ad Insertion, SSAI) 방식을
많이 씁니다. 이 경우:
- 광고 요청과 영상 요청이 도메인 레벨로는 구분되지 않아서, **DNS 차단으로는
  원천적으로 구분이 불가능**합니다.
- 완전한 HTTPS 내용 검사(이번 MVP에 없음)로 요청 URL/매니페스트까지 들여다봐도,
  구글이 광고/콘텐츠 URL 패턴을 자주 바꾸고 토큰으로 서명해서 신뢰성 있게
  구분하기 어렵습니다.

**필요한 추가 작업** (다음 단계로 분리 권장):
1. HTTPS MITM 옵션을 실제로 구현(위 설계대로, 기본 OFF + 명시적 CA 신뢰 절차).
2. 유튜브의 `/videoplayback`, `/get_video_info` 등 매니페스트 응답을 파싱해서
   광고 구간 메타데이터를 식별하는 로직 — 유지보수 부담이 크고 깨지기 쉬움.
3. 또는 SponsorBlock류 커뮤니티 데이터베이스를 활용하는 완전히 다른 접근
   (재생 중 광고 구간을 건너뛰는 방식) — 네트워크 차단이 아니라 플레이어 제어이므로
   별도 설계가 필요.

## 8. 브라우저별 테스트 결과

| 브라우저 | 일반 광고/추적 도메인 차단 | 사이트 정상 동작 | 유튜브 광고 | 비고 |
|---|---|---|---|---|
| Chrome  | **확인됨** (criteo/doubleclick/GTM/한국어 필터 도메인 등) | **확인됨** (dcinside, ruliweb) | 미확인 (예상: 차단 안 됨) | |
| Edge    | 미확인 | 미확인 | 미확인 (예상: 차단 안 됨) | |
| Firefox | **확인됨** | **확인됨** | 미확인 (예상: 차단 안 됨) | 내장 DoH를 껐는지는 미확인 — 꺼져 있지 않다면 차단이 간헐적일 수 있음 |

실사용 중 사이트 단위로 확인된 결과:

| 사이트 | 결과 | 비고 |
|---|---|---|
| dcinside.com | **버그로 사이트 전체가 차단됐다가 수정 후 정상 접속 확인** | 9장 참고 |
| naver.com | `g.tivan.naver.com`을 커스텀 규칙으로 추가하면 될 것으로 보임 — **아직 재테스트 전** | 로그에서 광고가 이 도메인에서 오는 것까지만 확인됨 |
| ruliweb.com | `image.ruliweb.com`을 커스텀 규칙에 추가 후 전면 배경광고 사라짐, 게시글/댓글 이미지 등 나머지 정상 | 루리웹 자체 광고 서버(실험적 차단, 아래 참고) |

## 9. 알려진 한계 / 충돌 가능성과 복구 방법

- **DNS 기반이라 같은 도메인에서 광고+콘텐츠를 같이 주는 서비스(유튜브 등)는
  차단되지 않습니다.** "모든 광고 100% 차단"을 주장하지 않습니다.
- **DNS-over-HTTPS/DNS-over-TLS를 쓰는 앱·브라우저 설정**은 우리 DNS 프록시를
  우회할 수 있습니다. Firefox의 내장 DoH가 대표적 — 꺼두어야 합니다.
- **VPN/보안 소프트웨어**가 자체적으로 시스템 DNS를 다시 바꾸는 경우 서로
  충돌할 수 있습니다. 이런 프로그램은 PolarAd을 켜기 전에 끄거나, 두 프로그램
  설정에서 DNS를 서로 양보하도록 조정하세요.
- **인증서 고정(certificate pinning) 앱, 금융 앱**: 이번 MVP는 HTTPS 내용을
  전혀 들여다보지 않으므로(순수 DNS 레벨) 이런 앱과 충돌하지 않습니다. 향후
  HTTPS MITM 옵션을 추가하면 그때부터 예외 목록이 필요합니다(5장 설계 참고).
- **복구 방법**:
  1. 정상 종료: 트레이 → "종료 (네트워크 설정 복구 후)" — 자동 복구.
  2. 창에서 "보호 기능 끄기" — 바로 복구.
  3. 설정 탭 → "네트워크 설정 긴급 복구" 버튼 — 앱이 살아있는 상태에서 강제 복구.
  4. 앱이 죽었거나 강제 종료된 경우: 앱을 다시 실행하면 시작 시 자동으로 백업
     파일을 확인해서 복구를 시도합니다(`ProtectionService.ReconcileStartupStateAsync`).
  5. 위 방법이 모두 안 통하면: 관리자 권한 PowerShell에서
     `scripts\restore-dns.ps1` 실행 — 앱 없이 모든 어댑터 DNS를 "자동(DHCP)"으로
     되돌립니다.

### 실사용 중 발견·수정한 문제

**버그: AdBlock 문법 목록의 `$modifier` 규칙을 잘못 해석해서 dcinside.com 전체가
차단됨.** 기본 소스로 추가한 AdGuard 한국어 필터 목록에 `||dcinside.com^$cookie=gaejuki_ad`
(쿠키 하나만 지우라는 아주 좁은 규칙)가 있었는데, 초기 버전의 파서가 `$` 뒤에
오는 조건을 구분하지 않고 "그래도 도메인 전체를 차단"으로 처리해버려서
dcinside.com 접속 자체가 막혔습니다. DNS 싱크홀은 도메인 전체를 막거나 말거나
둘 중 하나만 할 수 있어서, 조건(요청 타입/쿠키/제3자 여부 등)이 붙은 규칙은
안전하게 재현할 방법이 없습니다. **수정**: `$`로 시작하는 조건이 하나라도 붙은
규칙은 전부 무시하고, 조건 없는 순수 `||domain^` 규칙만 차단으로 인정하도록
`AdblockRuleParser`를 변경했습니다. `AdblockRuleParserTests.Rules_with_any_modifier_are_never_treated_as_a_full_domain_block`
가 이 문제의 회귀 테스트입니다.

**버그: 차단목록 업데이트 후 DNS 캐시가 안 지워짐.** 업데이트 직후 이미
캐시된 (예전엔 허용이었던) 도메인이 TTL 동안 계속 통과되는 문제가 있어서,
`UpdateBlocklistAsync` 성공 시 자동으로 `ipconfig /flushdns`를 실행하도록
추가했습니다. 단, 이건 Windows OS 캐시만 지우고 브라우저 자체의 DNS 캐시는
못 지웁니다 — 브라우저 탭을 완전히 닫았다 열거나(Chrome은
`chrome://net-internals/#dns` → "Clear host cache") 해야 할 수 있습니다.

**발견: 같은 도메인 광고는 사이트마다 패턴이 다름.** 네이버는 광고 서버가
`g.tivan.naver.com`처럼 콘텐츠 CDN(`pstatic.net`)과 분리돼 있어서 커스텀
규칙으로 깔끔하게 막힙니다. 반면 루리웹은 전면 배경광고 스크립트/배너를
`image.ruliweb.com`, 클릭 처리를 `api.ruliweb.com`으로, 광고 전용처럼 보이는
서브도메인에서 직접 서빙합니다 — `image.ruliweb.com`을 통째로 막아본 결과
광고는 사라지고 다른 기능(게시글/댓글 이미지 등)은 정상이었지만, 이건 사이트별로
직접 확인해야 하는 **실험적** 차단입니다(다른 사이트에서 같은 패턴의 도메인을
막으면 콘텐츠가 같이 깨질 수 있음 — 깨지면 바로 허용목록에 추가해서 되돌리세요).

## 10. 데이터는 어디에 저장되나요

전부 로컬에만 저장되고, 방문 기록이나 HTTPS 내용은 어디로도 전송되지 않습니다.

```
%LocalAppData%\PolarAd\
  settings.json           설정 (차단목록 소스 URL, 포트 등)
  blocklist_cache.txt     내려받은 공개 차단목록 캐시
  user_rules.json         내가 추가한 차단 규칙
  allowlist.json          허용 목록
  block_log.jsonl         최근 차단/허용 기록 (최대 설정 개수까지)
  dns_backup.json         보호 기능을 켜기 전 원래 DNS 설정 백업 (복구용)
```

## 11. 제거 방법

1. 트레이에서 "종료 (네트워크 설정 복구 후)"로 정상 종료합니다 (DNS가 자동
   복구됩니다).
2. `PolarAd` 폴더와 `%LocalAppData%\PolarAd` 폴더를 삭제합니다.
3. 혹시 DNS가 원래대로 안 돌아온 것 같으면 관리자 권한으로
   `scripts\restore-dns.ps1`을 실행하세요.

## 12. 남은 문제와 다음 개발 단계

1. **실사용 검증**: 이 세션은 UAC/실제 브라우저 상호작용을 자동화하지 않았으므로,
   사용자가 직접 켜고 Chrome/Edge/Firefox에서 확인해야 합니다 (6장 절차).
2. **WinDivert 기반 DoH 차단**: Firefox 등의 내장 DoH를 강제로 우회시켜 DNS
   차단이 항상 적용되도록 하는 보완 레이어.
3. **HTTPS MITM 옵션**: 설계만 있고 미구현. Titanium Web Proxy 등으로 OFF 기본
   옵션 구현, 로컬 CA 생성/신뢰 UX, 예외 목록(금융/VPN/인증서 고정 앱) 추가.
4. **유튜브 광고**: SSAI 특성상 네트워크 차단만으로는 해결이 어려움 — 별도
   접근(7장) 필요.
5. **설치 프로그램화**: 현재는 `dotnet publish` 결과 exe를 그냥 실행하는
   방식입니다. MSIX/Inno Setup 등으로 정식 설치 프로그램을 만들고, Windows
   시작 시 자동 실행 옵션을 추가할 수 있습니다.
6. **차단 목록 다양화**: 현재 기본값은 StevenBlack hosts 하나뿐입니다. 설정
   탭에서 URL을 추가할 수 있지만, 유튜브/트래커 전용 목록 등을 기본으로 더
   추가하면 커버리지가 늘어납니다.
