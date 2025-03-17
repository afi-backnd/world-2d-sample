using UnityEngine;
using BACKND;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Tilemaps;

public class GameNetworkManager : NetworkManager
{
    [Header("Game Manager")]
    public GameObject JoystickPrefab;

    [Header("Monster Settings")]
    public int MonsterMaxCount = 100;
    public float MonsterSpawnDelay = 2f; // 몬스터 재생성 딜레이
    public float MinDistanceFromPlayers = 25f; // 플레이어로부터 최소 거리
    public float SpawnRadius = 50f; // 몬스터 스폰 반경 (0,0에서부터의 거리)

    private List<GameObject> activeMonsters = new List<GameObject>();
    private bool isServerRunning = false;
    private Coroutine monsterManagerCoroutine;
    private Bounds groundBounds;

    public override void Awake()
    {
        // 화면 비율 설정
        SetCameraRect();

        base.Awake();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        isServerRunning = true;
        
        // 그라운드 경계 계산 - 통일된 방식으로 처리
        CalculateGroundBounds();
        
        // 서버 시작 시 몬스터 관리 코루틴 시작
        monsterManagerCoroutine = StartCoroutine(MonsterManagerRoutine());
    }
    
    // 그라운드 경계 계산 메서드 - 통일된 방식
    private void CalculateGroundBounds()
    {
        GameObject ground = GameObject.Find("Ground");
        if (ground != null)
        {
            // Grid에서 모든 Tilemap 찾기
            Tilemap[] tilemaps = ground.GetComponentsInChildren<Tilemap>();
            if (tilemaps != null && tilemaps.Length > 0)
            {
                // 모든 타일맵의 경계를 합쳐서 전체 경계 계산
                Bounds combinedBounds = new Bounds();
                bool firstBound = true;
                
                foreach (Tilemap tilemap in tilemaps)
                {
                    if (tilemap.GetComponent<TilemapRenderer>() != null)
                    {
                        // 타일맵의 사용된 영역 계산
                        tilemap.CompressBounds();
                        
                        // 타일맵의 실제 사용된 영역의 경계 가져오기
                        if (firstBound)
                        {
                            // 로컬 좌표의 경계를 월드 좌표로 변환
                            Vector3 min = tilemap.transform.TransformPoint(tilemap.cellBounds.min);
                            Vector3 max = tilemap.transform.TransformPoint(tilemap.cellBounds.max);
                            combinedBounds = new Bounds();
                            combinedBounds.SetMinMax(min, max);
                            firstBound = false;
                        }
                        else
                        {
                            // 로컬 좌표의 경계를 월드 좌표로 변환
                            Vector3 min = tilemap.transform.TransformPoint(tilemap.cellBounds.min);
                            Vector3 max = tilemap.transform.TransformPoint(tilemap.cellBounds.max);
                            Bounds tileBounds = new Bounds();
                            tileBounds.SetMinMax(min, max);
                            
                            combinedBounds.Encapsulate(tileBounds);
                        }
                    }
                }
                
                groundBounds = combinedBounds;
            }
        }
        
        // 그라운드가 없거나 계산 실패 시 기본 경계 설정
        if (groundBounds.size.x <= 0 || groundBounds.size.y <= 0)
        {
            groundBounds = new Bounds(Vector3.zero, new Vector3(SpawnRadius * 2, SpawnRadius * 2, 0));
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        isServerRunning = false;
        
        // 서버 중지 시 코루틴 중단
        if (monsterManagerCoroutine != null)
        {
            StopCoroutine(monsterManagerCoroutine);
            monsterManagerCoroutine = null;
        }
        
        // 모든 몬스터 제거
        ClearAllMonsters();
    }

    private void SetCameraRect()
    {
        Camera mainCamera = Camera.main;

        // 세로 모드에서는 9:16 비율 유지
        float targetAspect = 9.0f / 16.0f;
        float windowAspect = (float)Screen.width / (float)Screen.height;
        float scaleWidth = windowAspect / targetAspect;

        if (scaleWidth < 1.0f)
        {
            Rect rect = mainCamera.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;
            mainCamera.rect = rect;
        }
        else
        {
            float scaleHeight = 1.0f / scaleWidth;
            Rect rect = mainCamera.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;
            mainCamera.rect = rect;
        }
    }

    // 몬스터 관리 코루틴 - 몬스터 수를 계속 확인하고 부족하면 스폰
    [Server]
    private IEnumerator MonsterManagerRoutine()
    {
        // 초기 몬스터 생성
        SpawnInitialMonsters();
        
        while (isServerRunning)
        {
            // 죽은 몬스터 목록에서 제거
            CleanupDeadMonsters();
            
            // 몬스터 수가 최대치보다 적으면 추가 생성
            int monstersToSpawn = MonsterMaxCount - activeMonsters.Count;
            
            if (monstersToSpawn > 0)
            {
                for (int i = 0; i < monstersToSpawn; i++)
                {
                    SpawnMonster();
                    // 서버 부하를 줄이기 위해 약간의 딜레이 추가
                    yield return new WaitForSeconds(0.1f);
                }
            }
            
            // 정기적으로 체크
            yield return new WaitForSeconds(MonsterSpawnDelay);
        }
    }

    // 초기 몬스터 생성
    [Server]
    private void SpawnInitialMonsters()
    {
        for (int i = 0; i < MonsterMaxCount; i++)
        {
            SpawnMonster();
        }
    }

    // 단일 몬스터 생성
    [Server]
    private void SpawnMonster()
    {
        List<string> spawnablePrefabNames = new List<string>() { "Enemy 0", "Enemy 1", "Enemy 2" };
        
        // 랜덤 몬스터 타입 선택
        string selectedMonsterName = spawnablePrefabNames[Random.Range(0, spawnablePrefabNames.Count)];
        GameObject monsterPrefab = spawnPrefabs.Find(prefab => prefab.name == selectedMonsterName);
        
        if (monsterPrefab == null)
        {
            return;
        }
        
        // 스폰 위치 결정 (그라운드 크기 기반)
        Vector3 spawnPosition = GetValidSpawnPosition();
        
        // 몬스터 생성 및 네트워크 스폰
        GameObject monster = Instantiate(monsterPrefab, spawnPosition, Quaternion.identity);
        
        // 몬스터에게 그라운드 경계 정보 전달
        Monster monsterComponent = monster.GetComponent<Monster>();
        if (monsterComponent != null)
        {
            monsterComponent.SetGroundBounds(groundBounds);
        }
        
        NetworkServer.Spawn(monster);
        
        // 활성 몬스터 목록에 추가
        activeMonsters.Add(monster);
    }

    // 유효한 스폰 위치 찾기 (그라운드 크기 기반)
    [Server]
    private Vector3 GetValidSpawnPosition()
    {
        // 최대 시도 횟수 설정
        int maxAttempts = 30;
        
        // 스폰 중심점 (맵의 중앙)
        Vector3 spawnCenter = groundBounds.center;
        
        // 그라운드 크기와 SpawnRadius 중 작은 값 사용
        float mapWidth = groundBounds.size.x;
        float mapHeight = groundBounds.size.y;
        float effectiveRadius = Mathf.Min(SpawnRadius, Mathf.Min(mapWidth, mapHeight) / 2);
        
        for (int i = 0; i < maxAttempts; i++)
        {
            // 원 내부에 랜덤하게 위치 생성 (균등 분포를 위해 sqrt 사용)
            float angle = Random.Range(0f, Mathf.PI * 2);
            float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * effectiveRadius;
            
            float posX = spawnCenter.x + Mathf.Cos(angle) * distance;
            float posY = spawnCenter.y + Mathf.Sin(angle) * distance;
            
            // 그라운드 경계 내로 제한
            float borderMargin = 1.0f;
            posX = Mathf.Clamp(posX, groundBounds.min.x + borderMargin, groundBounds.max.x - borderMargin);
            posY = Mathf.Clamp(posY, groundBounds.min.y + borderMargin, groundBounds.max.y - borderMargin);
            
            Vector3 position = new Vector3(posX, posY, 0);
            
            // 플레이어가 있는 경우에만 플레이어와의 거리 체크
            bool isTooCloseToPlayer = false;
            PlayerController[] players = FindObjectsOfType<PlayerController>();
            
            if (players.Length > 0)
            {
                foreach (var player in players)
                {
                    if (Vector3.Distance(position, player.transform.position) < MinDistanceFromPlayers)
                    {
                        isTooCloseToPlayer = true;
                        break;
                    }
                }
            }
            
            // 다른 몬스터와의 거리도 확인
            bool isTooCloseToMonster = false;
            float minMonsterDistance = 2.0f; // 몬스터 간 최소 거리
            
            foreach (var monster in activeMonsters)
            {
                if (monster != null && Vector3.Distance(position, monster.transform.position) < minMonsterDistance)
                {
                    isTooCloseToMonster = true;
                    break;
                }
            }
            
            // 유효한 위치라면 반환 (플레이어가 없으면 플레이어 거리 체크 무시)
            if ((!isTooCloseToPlayer || players.Length == 0) && !isTooCloseToMonster)
            {
                return position;
            }
        }
        
        // 최대 시도 횟수를 초과하면 그냥 랜덤 위치 반환
        float fallbackAngle = Random.Range(0f, Mathf.PI * 2);
        float fallbackDistance = Mathf.Sqrt(Random.Range(0f, 1f)) * effectiveRadius;
        
        float fallbackX = spawnCenter.x + Mathf.Cos(fallbackAngle) * fallbackDistance;
        float fallbackY = spawnCenter.y + Mathf.Sin(fallbackAngle) * fallbackDistance;
        
        // 그라운드 경계 내로 제한
        float fbBorderMargin = 1.0f;
        fallbackX = Mathf.Clamp(fallbackX, groundBounds.min.x + fbBorderMargin, groundBounds.max.x - fbBorderMargin);
        fallbackY = Mathf.Clamp(fallbackY, groundBounds.min.y + fbBorderMargin, groundBounds.max.y - fbBorderMargin);
        
        return new Vector3(fallbackX, fallbackY, 0);
    }

    // 죽은 몬스터 정리
    [Server]
    private void CleanupDeadMonsters()
    {
        activeMonsters.RemoveAll(monster => monster == null);
    }

    // 모든 몬스터 제거
    [Server]
    private void ClearAllMonsters()
    {
        foreach (var monster in activeMonsters)
        {
            if (monster != null)
            {
                NetworkServer.Destroy(monster);
            }
        }
        
        activeMonsters.Clear();
    }
}
