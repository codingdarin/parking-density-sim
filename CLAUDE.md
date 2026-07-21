# parking-density-sim

로봇 주차장 비상 대응 밀도 시뮬레이터. 상세는 docs/ 참조:
- [기획정의서](docs/기획정의서.md) — 왜 만드는가
- [개발계획서](docs/개발계획서.md) — 일정(D0~D10)·아키텍처·리스크. **작업 전 현재 일차의 완료 기준 확인**
- [측정정의서](docs/측정정의서.md) — 측정·집계 규칙, 모델 상수. **수치 관련 코드는 이 문서가 기준**

## 구조

- `ConsoleSim/` — .NET 8 콘솔. **D1~D4의 주 작업 환경** (Unity 열지 않음). 실행: `dotnet run --project ConsoleSim`
- `ParkingSim/` — Unity 프로젝트 (D5부터). `Assets/Scripts/Core/`가 시뮬레이션 코어
- `output/` — 실험 CSV

## 코드 규칙 (어기기 쉬운 것들)

1. **`Core/`는 Unity 비의존 순수 C#.** `using UnityEngine` 금지 — ConsoleSim 빌드가 컴파일 타임 가드이므로, Core 수정 후 반드시 `dotnet build ConsoleSim`으로 검증
2. **C# 9 문법까지만** (Unity 호환). 암시적 using 금지, 최상위 문 금지 — csproj에 고정돼 있음
3. **랜덤은 시드 주입된 `System.Random`만.** `UnityEngine.Random`·`DateTime.Now` 기반 시드 금지 (재현성: 같은 시드 → 같은 결과)
4. **시간은 틱으로 기록.** 초 환산(×2.5초/셀)은 표시·분석 단계에서만
5. **Unity 에디터 의존 최소화**: 오브젝트는 코드 생성(`GameObject.CreatePrimitive`), 프리팹·인스펙터 연결 지양. asmdef는 P2 전까지 추가하지 않음
6. Unity 쪽 빌드 산출물(`bin/obj`)이 `Assets/` 안에 생기지 않도록 ConsoleSim은 형제 디렉터리 유지

## 커밋 컨벤션

- 형식: `<type>: <한국어 요약>` — type은 `feat` `fix` `docs` `test` `refactor` `chore`
- **작동하는 상태로만 커밋** (빌드 깨진 채 커밋 금지, 커밋 전 `dotnet build ConsoleSim`)
- 커밋·푸시는 사용자가 요청할 때만
- **`Co-Authored-By` 등 AI 크레딧 라인을 커밋 메시지에 넣지 않는다**

## 일정 규율

- P0 미완이면 P1 착수 금지. P0가 막히면 **당일 중** 폴백 결정 (계획서 리스크 표 참조) — 다음날로 미루지 않음
- 범위 확장 금지: 비주얼 개선·연출은 D10 이후에만
