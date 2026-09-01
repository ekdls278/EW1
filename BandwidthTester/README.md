# 대역폭 테스트 시뮬레이터 (BandwidthTester)

통신 대역폭 테스트를 위한 모의 소프트웨어입니다. Windows GUI(WPF) + JSON 설정 파일 조합으로 만들었습니다.

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
    BandwidthTester.Gui/    # WPF GUI (Windows 전용, net8.0-windows)
    BandwidthTester.Cli/    # 콘솔 버전 (크로스플랫폼, GUI 없이 config.json만으로 실행)
  tests/
    BandwidthTester.Tests/  # xUnit 테스트 (헤더 인코딩/디코딩, 설정 파일, 실제 TCP/UDP 루프백 통신)
  samples/
    config.sample.json         # 예제 설정 파일 (TCP/UDP, 서버/클라이언트, 커스텀 헤더 예시 포함)
    quickstart-loopback.json   # 바로 실행해볼 수 있는 127.0.0.1 client+server 페어 (다른 PC 없이 자체 테스트용)
```

**중요:** `BandwidthTester.Core`, `BandwidthTester.Cli`, `BandwidthTester.Tests`는 이 저장소를 작성한 Linux
컨테이너에서 실제로 빌드하고 `dotnet test`까지 통과시켜 검증했습니다 (14/14 통과, 실제 루프백 TCP/UDP 송수신 포함).
`BandwidthTesterCli`는 `quickstart-loopback.json`으로 실제 소켓을 열어 목표 대역폭(2MB/s)에 근접한 실측 처리량이
나오는 것까지 확인했습니다. 반면 `BandwidthTester.Gui`는 WPF 기반이라 **Windows에서만 빌드/실행**할 수 있어 이
환경에서는 컴파일 검증을 하지 못했습니다. Windows에서 빌드 후 문제가 있으면 알려주시면 수정하겠습니다.

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

### GUI (Windows, .NET 8 SDK 필요)

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
3. 목록에서 각 소켓의 `시작`/`정지` 버튼으로 개별 제어하거나, 상단 `전체 시작`/`전체 정지`로 한 번에 제어합니다.
4. `설정 저장` / `설정 불러오기`로 소켓 목록 전체를 JSON 파일로 저장·복원할 수 있습니다 (개수 제한 없음).
5. 목록에는 각 소켓의 상태, 초당 송/수신 속도, 누적 송/수신 바이트, 상대방 주소, 마지막 오류가 실시간으로 표시됩니다.
   하단 로그창에 연결/종료/재시도 등의 이벤트가 기록됩니다.

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
