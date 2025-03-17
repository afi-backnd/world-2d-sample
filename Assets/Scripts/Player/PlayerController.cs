using System.Collections;
using UnityEngine;
using BACKND;
using SimpleInputNamespace;
using UnityEngine.Tilemaps;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    
    [Header("Camera Settings")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0, 0, -10);
    [SerializeField] private float cameraSmoothing = 0.125f;
    [SerializeField] private Vector2 cameraBounds = new Vector2(10, 10); // 기본 경계 제한

    [Header("Meteor System")]
    [SerializeField] private float meteorSpawnInterval = 0.5f; // 0.5초마다 메테오 생성

    // 로컬 입력값
    private Vector2 movement;
    private Rigidbody2D rb;
    private Joystick joystick;
    private bool isFacingRight = true;
    private Camera mainCamera;
    private Coroutine meteorCoroutine;
    private bool meteorSystemActive = true;
    private Bounds groundBounds;
    
    public override void OnStartLocalPlayer()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
        
        // 그라운드 경계 계산 - GameNetworkManager와 동일한 방식으로 처리
        CalculateGroundBounds();

        GameNetworkManager networkManager = NetworkManager.Instance as GameNetworkManager;
        if (networkManager != null)
        {
            networkManager.JoystickPrefab.SetActive(true);
            joystick = networkManager.JoystickPrefab.GetComponent<Joystick>();
        }
        
        // 로컬 플레이어일 경우 카메라 설정
        if (mainCamera != null)
        {
            // 카메라 초기 위치 설정
            UpdateCameraPosition();
        }
        
        meteorCoroutine = StartCoroutine(AutoSpawnMeteors());
    }

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
            groundBounds = new Bounds(Vector3.zero, new Vector3(40, 40, 0));
        }
    }

    private void OnDestroy()
    {
        if (meteorCoroutine != null)
        {
            StopCoroutine(meteorCoroutine);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        // 조이스틱 입력 받기
        movement.x = joystick.xAxis.value;
        movement.y = joystick.yAxis.value;
        
        // X축 이동 방향에 따라 Y축 회전 변경
        if (movement.x > 0.1f && !isFacingRight)
        {
            isFacingRight = true;
            transform.rotation = Quaternion.Euler(0, 0, 0);
        }
        else if (movement.x < -0.1f && isFacingRight)
        {
            isFacingRight = false;
            transform.rotation = Quaternion.Euler(0, 180, 0);
        }
    }

    private void LateUpdate()
    {
        if (!isLocalPlayer) return;
        
        // 카메라 위치 업데이트
        UpdateCameraPosition();
    }

    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;

        // 정규화된 방향 벡터로 이동
        if (movement.magnitude > 0.1f)
        {
            Vector2 newPosition = (Vector2)transform.position + movement.normalized * moveSpeed * Time.fixedDeltaTime;
            
            // 플레이어가 Ground 경계를 벗어나지 않도록 제한
            if (groundBounds.size.x > 0 && groundBounds.size.y > 0)
            {
                float playerRadius = 0.5f; // 플레이어 콜라이더 반경 (조정 필요)
                newPosition.x = Mathf.Clamp(newPosition.x, 
                                          groundBounds.min.x + playerRadius, 
                                          groundBounds.max.x - playerRadius);
                newPosition.y = Mathf.Clamp(newPosition.y, 
                                          groundBounds.min.y + playerRadius, 
                                          groundBounds.max.y - playerRadius);
            }
            
            rb.MovePosition(newPosition);
        }
        else
        {
            rb.velocity = Vector2.zero;
        }
    }
    
    private void UpdateCameraPosition()
    {
        if (mainCamera == null) return;
        
        // 목표 카메라 위치 계산
        Vector3 targetPosition = transform.position + cameraOffset;
        
        // 부드러운 카메라 이동
        Vector3 smoothedPosition = Vector3.Lerp(mainCamera.transform.position, targetPosition, cameraSmoothing);
        
        // 카메라 경계 제한 적용
        if (groundBounds.size.x > 0 && groundBounds.size.y > 0)
        {
            // 카메라의 보이는 영역 계산
            float cameraHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
            float cameraHalfHeight = mainCamera.orthographicSize;
            
            // 카메라가 맵 경계를 벗어나지 않도록 제한
            float minX = groundBounds.min.x + cameraHalfWidth;
            float maxX = groundBounds.max.x - cameraHalfWidth;
            float minY = groundBounds.min.y + cameraHalfHeight;
            float maxY = groundBounds.max.y - cameraHalfHeight;
            
            // 맵이 카메라보다 작은 경우 중앙에 고정
            if (minX > maxX) 
            {
                float centerX = (groundBounds.min.x + groundBounds.max.x) * 0.5f;
                smoothedPosition.x = centerX;
            }
            else
            {
                smoothedPosition.x = Mathf.Clamp(smoothedPosition.x, minX, maxX);
            }
            
            if (minY > maxY)
            {
                float centerY = (groundBounds.min.y + groundBounds.max.y) * 0.5f;
                smoothedPosition.y = centerY;
            }
            else
            {
                smoothedPosition.y = Mathf.Clamp(smoothedPosition.y, minY, maxY);
            }
        }
        else
        {
            // 유효한 경계가 없는 경우 기본 경계 적용
            smoothedPosition.x = Mathf.Clamp(smoothedPosition.x, -cameraBounds.x, cameraBounds.x);
            smoothedPosition.y = Mathf.Clamp(smoothedPosition.y, -cameraBounds.y, cameraBounds.y);
        }
        
        // 카메라 위치 적용 (Z값은 유지)
        mainCamera.transform.position = new Vector3(
            smoothedPosition.x,
            smoothedPosition.y,
            cameraOffset.z
        );
    }

    // 클라이언트에서 서버로 메테오 생성 요청을 보내는 Command
    [Command]
    private void CmdSpawnMeteor()
    {
        // 서버에서 실행될 코드
        Vector3 meteorSpawnPosition = transform.position + new Vector3(-2, 2, 0);

        // 메테오 생성
        GameObject meteorObject = NetworkManager.Instance.spawnPrefabs.Find(prefab => prefab.name == "Meteor");
        if (meteorObject == null)
        {
            return;
        }

        meteorObject = Instantiate(meteorObject, meteorSpawnPosition, Quaternion.identity);
    
        Meteor meteor = meteorObject.GetComponent<Meteor>();
        if (meteor != null)
        {
            meteor.Initialize(meteorSpawnPosition);
        }

        NetworkServer.Spawn(meteorObject);
    }

    // AutoSpawnMeteors 코루틴 수정
    private IEnumerator AutoSpawnMeteors()
    {
        while (meteorSystemActive)
        {
            // 메테오 생성 요청
            CmdSpawnMeteor();
            
            // 다음 메테오 생성까지 대기
            yield return new WaitForSeconds(meteorSpawnInterval);
        }
    }
}
