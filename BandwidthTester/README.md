# 대역폭 테스트 시뮬레이터 (BandwidthTester)

통신 대역폭 테스트를 위한 모의 소프트웨어입니다. Windows GUI + JSON 설정 파일 조합으로 만들었습니다.
GUI는 두 가지가 있습니다: **BandwidthTester.AvaloniaGui**(권장 — 크로스플랫폼 Avalonia 기반, 이 저장소를
작성한 Linux 컨테이너에서 실제로 빌드·실행·화면 렌더링까지 검증됨)와 **BandwidthTester.Gui**(WPF, Windows에서만
빌드 가능해 이 환경에서는 컴파일 검증을 하지 못함).

## 요구사항 대응표

| 요구사항 | 구현 |
|---|---|
| 1. local ip / remote ip / port / client-server / TCP-UDP 설정 후 소켓 연결 | `SocketProfile` + `SocketWorker` (TCP/UDP, Client/Server 모두 지원) |
| 2. 서로 다른 설정을 무제한으로 추가 | `SocketSessionManager`가 `Guid` 키의 소켓들을 무제한 보유·시작·정지 |
| 3. 소켓별 메시지 크기 / 대역폭 설정 후 송신 | `SocketProfile.MessageSize`, `TargetBandwidthBytesPerSec` + `BandwidthLimiter`(토큰 버킷 페이싱) |
| 4. 송/수신 각각 리틀엔디안·빅엔디안 설정 | `SocketProfile.SendByteOrder` / `ReceiveByteOrder`, 헤더 필드마다 적용 |
| 5. 송신 시 앞 20바이트는 사용자가 구조체 정의 | `HeaderDefinition`/`HeaderFieldDefinition` — 필드명·타입·크기·고정값 또는 자동값(시퀀스/타임스탬프/페이로드 길이)을 GUI에서 자유롭게 구성, 총합 20바이트 검증 |

## 프로젝트 구조

```
BandwidthTester/
  BandwidthTester.sln
  src/
    BandwidthTester.Core/   # 크로스플랫폼 핵심 로직 (소켓/헤더/대역폭/설정) - Linux에서도 빌드·테스트됨
    BandwidthTester.AvaloniaGui/  # Avalonia GUI (크로스플랫폼, win-x64 exe 발행 가능) - 권장
    BandwidthTester.Gui/    # WPF GUI (Windows 전용, net8.0-windows)
    BandwidthTester.Cli/    # 콘솔 버전 (크로스플랫폼, GUI 없이 config.json만으로 실행)
  tests/
    BandwidthTester.Tests/  # xUnit 테스트 (헤더 인코딩/디코딩, 설정 파일, 실제 TCP/UDP 루프백 통신)
  samples/
    config.sample.json         # 예제 설정 파일 (TCP/UDP, 서버/클라이언트, 커스텀 헤더 예시 포함)
    quickstart-loopback.json   # 바로 실행해볼 수 있는 127.0.0.1 client+server 페어 (다른 PC 없이 자체 테스트용)
```

**중요:** `BandwidthTester.Core`, `BandwidthTester.Cli`, `BandwidthTester.AvaloniaGui`, `BandwidthTester.Tests`는
이 저장소를 작성한 Linux 컨테이너에서 실제로 빌드하고 검증했습니다:
- `dotnet test` 14/14 통과 (실제 루프백 TCP/UDP 송수신 포함).
- `BandwidthTesterCli`는 `quickstart-loopback.json`으로 실제 소켓을 열어 목표 대역폭(2MB/s)에 근접한 실측
  처리량이 나오는 것까지 확인.
- `BandwidthTester.AvaloniaGui`는 Xvfb(가상 디스플레이) + xdotool로 실제 창을 띄우고, 소켓 추가 다이얼로그
  입력, 20바이트 헤더 합계 실시간 검증, 소켓 목록 그리드 표시, `전체 시작` 클릭 후 상태가 `Connecting`으로
  바뀌고 로그 패널에 재시도 로그가 실시간으로 찍히는 것까지 화면 캡처로 확인했습니다.

반면 `BandwidthTester.Gui`(WPF)는 **Windows에서만 빌드 가능**해 이 환경에서는 컴파일 검증을 하지 못했습니다.
Windows에서 빌드 후 문제가 있으면 알려주시면 수정하겠습니다. GUI가 필요하면 `BandwidthTester.AvaloniaGui`를
우선 사용하시길 권장합니다.

## 빌드 및 실행

### CLI (콘솔, GUI 없이 바로 시험 가능)

```
cd BandwidthTester
dotnet publish src/BandwidthTester.Cli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/win-x64
publish/win-x64/BandwidthTesterCli.exe samples/quickstart-loopback.json
```

(`--self-contained false`로 빌드한 exe는 대상 PC에 [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)이
설치되어 있어야 합니다. `--self-contained true`로 빌드하면 런타임 설치 없이 바로 실행되지만 exe 용량이 약 65MB로 커집니다.)

`config.json`에 정의된 모든 소켓(`Enabled: true`인 것)을 시작하고, 1초마다 소켓별 상태/송수신 속도/누적량을 콘솔에
출력합니다. `Ctrl+C`로 정상 종료됩니다.

### GUI - Avalonia (권장, .NET 8 SDK만 있으면 Windows/Linux/macOS 어디서든 빌드 가능)

```
cd BandwidthTester
dotnet run --project src/BandwidthTester.AvaloniaGui
```

Windows용 실행파일(exe 1개, 약 25MB, .NET 8 Runtime 필요)을 만들려면:

```
dotnet publish src/BandwidthTester.AvaloniaGui -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/gui-win-x64
```

### GUI - WPF (Windows에서만 빌드 가능, 이 환경에서 검증 못함)

```
cd BandwidthTester
dotnet run --project src/BandwidthTester.Gui
```

또는 `BandwidthTester.sln`을 Visual Studio 2022(17.8+)로 열어서 `BandwidthTester.Gui`를 시작 프로젝트로 지정 후 실행(F5)하면 됩니다.

### Core / 테스트 (Windows/Linux/macOS 공통)

```
cd BandwidthTester
dotnet test tests/BandwidthTester.Tests
```

## GUI 사용법

1. **소켓 추가**: 상단 `소켓 추가` 버튼 → 이름/역할(Client·Server)/프로토콜(TCP·UDP)/Local IP·Port/Remote IP·Port/
   송신 사용 여부/메시지 크기/목표 대역폭(bytes/sec, 0=무제한)/송신·수신 엔디안을 입력합니다.
2. **20바이트 헤더 구조체 편집**: 같은 창 하단의 표에서 필드를 추가/삭제하며 이름·타입·크기(Bytes 타입만)·자동값
   (없음/시퀀스 번호/타임스탬프(ms)/페이로드 길이)·고정값을 설정합니다. 필드 크기 합이 정확히 20바이트가 되어야
   저장할 수 있습니다(상단에 실시간으로 "OK/20바이트가 되어야 합니다"가 표시됩니다).
3. **선택 복사**: 목록에서 소켓을 선택하고 `선택 복사`를 누르면 같은 설정(헤더 구조체 포함)을 가진 새 소켓이
   바로 옆에 추가됩니다. 이름/원격 IP 등만 바꿔가며 비슷한 소켓을 여러 개 만들 때 유용합니다.
4. 목록에서 각 소켓의 `시작`/`정지` 버튼으로 개별 제어하거나, 상단 `전체 시작`/`전체 정지`로 한 번에 제어합니다.
   실행 중이 아니면 `정지`가, 실행 중이면 `시작`이 자동으로 비활성화되어 지금 어떤 상태인지 헷갈리지 않습니다.
5. **자동 저장**: 소켓 추가·복사·편집·삭제 시 실행 파일과 같은 폴더의 `config.json`에 즉시 자동 저장됩니다.
   `설정 저장` 버튼을 따로 누를 필요가 없고, 그냥 껐다 켜도 마지막 상태가 그대로 복원됩니다. `설정 불러오기` /
   `다른 이름으로 저장`으로 다른 파일을 쓰면 이후 자동 저장 대상도 그 파일로 바뀝니다.
6. 목록에는 각 소켓의 상태, 초당 송/수신 속도(TX/RX 한 칸에 표시), 누적 송/수신 바이트, 상대방 주소, 마지막
   오류가 실시간으로 표시됩니다. 하단 로그창에 연결/종료/재시도 등의 이벤트가 기록됩니다.

## 설정 파일(config.json) 스키마

`samples/config.sample.json` 예시를 참고하세요. 핵심 필드:

- `Role`: `"Client"` | `"Server"`
- `Protocol`: `"Tcp"` | `"Udp"`
- `LocalIp` / `LocalPort`, `RemoteIp` / `RemotePort`
- `SendByteOrder` / `ReceiveByteOrder`: `"LittleEndian"` | `"BigEndian"`
- `MessageSize`: 헤더(20바이트)를 제외한 페이로드 크기(byte)
- `TargetBandwidthBytesPerSec`: 목표 송신 대역폭(byte/sec), `0`이면 무제한
- `SendEnabled`: `false`면 수신만 수행(대역폭 측정용 수신 전용 소켓)
- `Header.Fields[]`: 20바이트 헤더 구조체 정의
  - `Type`: `UInt8`/`Int8`/`UInt16`/`Int16`/`UInt32`/`Int32`/`UInt64`/`Int64`/`Float32`/`Float64`/`Bytes`
  - `Size`: `Bytes` 타입일 때만 사용(그 외는 타입에서 자동 결정)
  - `Auto`: `None`(고정값 사용)/`Sequence`(패킷 순번 자동 증가)/`TimestampMs`(송신 시각, ms)/`PayloadLength`(페이로드 크기 자동 기록)
  - `Value`: `Auto=None`일 때의 고정값. 정수는 10진수 또는 `0x` 16진수, `Bytes` 타입은 hex 문자열(길이는 `Size*2`)

## 동작 방식 참고

- TCP는 스트림이므로 송/수신 양쪽이 **같은 `MessageSize`**로 설정되어 있어야 프레임 경계가 올바르게 해석됩니다.
- UDP는 데이터그램 단위로 경계가 보존되므로 크기가 달라도 손실 없이 수신되지만, 통계/헤더 해석의 일관성을 위해
  역시 양쪽을 동일하게 맞추는 것을 권장합니다.
- 대역폭 제한은 리키버킷(leaky-bucket) 방식으로 페이싱하며, `0`을 설정하면 소켓/네트워크가 허용하는 최대 속도로 전송합니다.
- TCP 서버 소켓은 다중 클라이언트 접속을 동시에 처리합니다.
- `SocketProfile.Id`는 JSON에 저장되지 않습니다(내부 실행용 식별자일 뿐). 설정 파일을 손으로 복붙해서 소켓
  항목을 늘려도 ID 충돌 걱정 없이 그냥 불러오면 매번 새로 발급됩니다.
- 소켓/TCP 튜닝: 송수신 버퍼 1MB, TCP는 Nagle 알고리즘 비활성화(`NoDelay`)로 설정해 툴 자체가 병목이 되지
  않도록 했습니다. 이 컨테이너의 루프백 기준 무제한 모드로 1.3GB/s 이상, 20MB/s 목표 시 실측 19.1MB/s
  (오차 5% 이내)를 확인했습니다 — 실제 네트워크에서는 대개 회선/상대 장비가 병목이지 이 툴이 병목이 되진 않습니다.
