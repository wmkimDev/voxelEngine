# VoxelEngine

<img width="1215" height="957" alt="screenshot" src="https://github.com/user-attachments/assets/61a4bb93-495e-4039-8733-6b2e42a6848b" />

**절차적 월드 생성**, **청크 스트리밍**, **복셀 메시 최적화**를 중심으로 구현한 Unity 기반 복셀 엔진 프로젝트입니다.

이 프로젝트는  
**Unity에서 대규모 복셀 월드를 안정적으로 스트리밍하고, 메싱·재빌드·충돌 처리 비용을 줄이는 구조를 설계하고 구현한 프로젝트**입니다.

## 프로젝트 소개

VoxelEngine은 **Unity 6 / C#** 기반의 청크형 복셀 월드 런타임입니다.  
프로젝트의 핵심 목표는 다음과 같습니다.

- 확장 가능한 복셀 엔진 구조 만들기
- 넓은 복셀 월드를 효율적으로 렌더링하기
- 실제 엔진 병목을 줄일 수 있는 최적화 구조 적용하기

현재 엔진은 다음 기능을 지원합니다.

- 평지 / 구릉 / 산맥 기반 절차적 지형 생성
- 청크 단위 월드 스트리밍
- 여러 메셔 경로 지원
- Greedy Meshing 및 Job System 기반 메싱
- 런타임 블록 편집
- 거리 기반 콜라이더 관리
- 선형 포그 기반 대기감 연출
- 성능 HUD 및 병목 분석 도구

## 주요 기능

### 1. 청크 기반 월드 스트리밍

월드는 고정 크기 청크로 나뉘며, 플레이어 주변만 동적으로 로드/언로드됩니다.

- **로드 반경**과 **언로드 반경** 분리
- 시야 기반 로드 우선순위
- 바라보는 방향 우선 로드
- 최근 언로드 청크의 데이터 캐시

관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkManager.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkManager.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Streaming/ChunkLoadScheduler.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Streaming/ChunkLoadScheduler.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkLoadPlanner.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkLoadPlanner.cs)

### 2. 절차적 지형 생성

현재 생성기는 단순한 단일 노이즈 지형이 아니라,  
**넓은 평지, 완만한 구릉, 멀리 보이는 산맥**이 구분되도록 설계되어 있습니다.

- 다층 높이맵 기반 표면 생성
- 산지 마스크와 foothill 구간 분리
- ridged noise 기반 능선과 valley 보정
- 청크 단위 `surfaceHeight` / `soilDepth` 선계산

관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGenerator.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGenerator.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGeneratorSettings.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGeneratorSettings.cs)

### 3. 여러 메시 빌드 경로

엔진은 용도와 성능 특성에 따라 여러 메싱 경로를 지원합니다.

- `Naive`
- `Greedy`
- `JobNaive`
- `JobGreedy`

Greedy Meshing은 인접한 face를 병합해 quad 수를 줄이고,  
Job 기반 경로는 메시 생성 계산을 Unity Job System으로 분산합니다.

관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/NaiveMeshBuilder.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/NaiveMeshBuilder.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/GreedyMeshBuilder.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/GreedyMeshBuilder.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/Jobs/JobSystemMeshBuilder.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/Jobs/JobSystemMeshBuilder.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/Jobs/JobGreedyMeshBuilder.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/Jobs/JobGreedyMeshBuilder.cs)

### 4. 공통 Geometry 규칙 분리

face winding, 축 방향, greedy face geometry 규칙을 공용화해  
CPU 메셔와 Job 메셔가 동일한 규칙을 따르도록 정리했습니다.

관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/FaceTopology.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/FaceTopology.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/GreedyGeometry.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Meshing/GreedyGeometry.cs)

### 5. 런타임 블록 편집

플레이 중 블록을 제거하거나 배치할 수 있으며,  
이웃 청크 경계까지 고려해 재빌드를 예약합니다.

관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Editing/ChunkEditInteractor.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Editing/ChunkEditInteractor.cs)

## 적용한 최적화 기법

프로젝트에 적용한 핵심 최적화 기법은 다음과 같습니다.

- **Greedy Meshing**  
  인접한 동일 면을 큰 quad로 병합해 vertex/triangle 수를 줄입니다.

- **Unity Job System 기반 메싱**  
  메시 생성 계산을 Job 경로로 분리해 메인 스레드 부담을 줄였습니다.

- **Burst 컴파일 적용**  
  Job 기반 메싱 경로에 Burst를 적용해 순수 계산 구간의 실행 비용을 줄였습니다.

- **로드 반경 / 언로드 반경 분리**  
  스트리밍 경계에서 청크가 생성되었다가 바로 사라지는 떨림을 줄이기 위해 load/unload 반경을 분리했습니다.

- **ChunkData 캐시**  
  최근 언로드된 청크 데이터를 메모리에 보관해, 다시 같은 구역을 볼 때 생성 비용을 줄입니다.

- **스트리밍 집합 캐시**  
  플레이어가 같은 중심 청크 안에 있는 동안 `requiredChunks` / `unloadProtectedChunks`를 재계산하지 않고 재사용합니다.

- **언로드 delta 처리**  
  전체 loaded chunk를 매번 다시 순회하지 않고, 이전 unload 보호 집합과 현재 집합의 차이만 계산해 unload/rebuild 대상을 구합니다.

- **컬럼 맵 선계산**  
  청크 생성 전에 `surfaceHeight`와 `soilDepth`를 컬럼 단위로 먼저 계산해, 3D voxel 채우기 루프가 이 결과를 재사용하도록 구성했습니다.

- **메시 빌드 dedupe**  
  같은 청크에 대한 중복 rebuild 요청을 합쳐, 같은 프레임 또는 같은 pending 상태에서 반복 재빌드를 줄입니다.

- **초기 빌드와 일반 재빌드 분리**  
  새로 로드된 청크의 첫 메시 생성과 기존 청크 재빌드를 구분해 처리 우선순위를 나눴습니다.

- **시야 기반 로드 우선순위**  
  카메라 프러스텀 안쪽 청크를 먼저 로드하고, 그 안에서는 바라보는 방향과 거리 기준으로 우선순위를 정합니다.

- **거리 기반 collider 활성화**  
  가까운 청크만 collider를 유지하고 먼 청크는 렌더만 남겨 물리 비용을 줄입니다.

- **포그 기반 시야 완충**  
  선형 포그를 월드 설정과 연결해 먼 거리 스트리밍 경계가 덜 거슬리도록 조정했습니다.

성능 계측 관련 파일:

- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Performance/VoxelPerformanceHud.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Performance/VoxelPerformanceHud.cs)
- [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Performance/VoxelPerformanceStats.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Performance/VoxelPerformanceStats.cs)

## 아키텍처

코드베이스는 크게 두 레이어로 나뉩니다.

### Core

Unity API에 의존하지 않는 순수 엔진 레이어입니다.

- voxel 데이터
- 좌표와 neighborhood
- 스트리밍 정책
- 월드 생성
- 메시 빌더

### UnityBridge

Unity 런타임과 연결되는 레이어입니다.

- 스트리밍 오케스트레이션
- 청크 GameObject 관리
- Mesh / MeshCollider 적용
- Job 스케줄링
- 런타임 편집
- HUD / 성능 시각화

## 기술 스택

- **Engine:** Unity 6
- **Language:** C#
- **Rendering:** URP
- **Parallelism:** Unity Job System + Burst
- **Tests:** Unity EditMode Tests

## 현재 집중하고 있는 문제

현재 최적화의 초점은 다음과 같습니다.

- 불필요한 rebuild 빈도 줄이기
- 매 프레임 청크 관리 비용 줄이기
- 월드 생성 비용 줄이기
- 넓은 월드에서의 스트리밍 구조 개선
- 먼 거리 렌더링을 더 잘 버틸 수 있는 구조 만들기

## 프로젝트 성격

이 프로젝트는 Unity 환경에서 다음 문제를 직접 다루는 작업입니다.

- 엔진 레벨 구조 설계
- 데이터 중심 최적화
- 구조적 성능 개선
- 시뮬레이션 로직과 Unity 런타임 계층 분리

즉, 복셀 렌더링, 청크 스트리밍, rebuild scheduling, 성능 최적화를 실제 엔진 구조로 구현하는 데 초점을 두고 있습니다.

## 빠르게 볼 파일

프로젝트를 처음 볼 때는 아래 파일부터 보면 전체 흐름을 빠르게 이해할 수 있습니다.

1. [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkManager.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Streaming/ChunkManager.cs)
2. [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Settings/VoxelWorldSettings.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Settings/VoxelWorldSettings.cs)
3. [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/ChunkMeshController.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/ChunkMeshController.cs)
4. [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/ChunkMeshPresenter.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/UnityBridge/Meshing/ChunkMeshPresenter.cs)
5. [/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGenerator.cs](/Users/wmk/Desktop/Projects/VoxelEngine/Assets/Scripts/Core/Generation/NoiseWorldGenerator.cs)

## 라이선스

이 프로젝트는 MIT License를 따릅니다. 자세한 내용은 [/Users/wmk/Desktop/Projects/VoxelEngine/LICENSE](/Users/wmk/Desktop/Projects/VoxelEngine/LICENSE)를 참고하세요.
