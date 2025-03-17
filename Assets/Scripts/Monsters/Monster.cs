using System.Collections;
using UnityEngine;
using BACKND;

public class Monster : NetworkBehaviour
{
    [Header("Stats")]
    public int maxHealth = 100;
    [SyncVar(hook = nameof(OnHealthChanged))]
    public int currentHealth;
    public float moveSpeed = 2f;
    public float detectionRadius = 5f;
    public float stunDuration = 0.5f;

    // 상태 변수
    [SyncVar]
    private bool isStunned = false;
    
    // 색상 동기화를 위한 SyncVar
    [SyncVar(hook = nameof(OnColorChanged))]
    private Color monsterColor = Color.white;
    
    // 이동 관련 변수
    private Vector3 targetPosition;
    private float nextPositionChangeTime = 0f;
    private float positionChangeInterval = 3f;
    private Bounds groundBounds;
    private bool hasGroundBounds = false;
    
    // 컴포넌트 참조
    private SpriteRenderer spriteRenderer;
    private GameObject currentTarget;
    
    // 초기화
    public override void OnStartServer()
    {
        base.OnStartServer();
        currentHealth = maxHealth;
        
        // 서버에서만 초기 위치 설정
        ChooseNewPosition();
    }
    
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 클라이언트 시작 시 현재 색상 적용
        spriteRenderer.color = monsterColor;
    }
    
    // 색상 변경 훅 메서드
    private void OnColorChanged(Color oldColor, Color newColor)
    {
        spriteRenderer.color = newColor;
    }
    
    // 그라운드 경계 설정 메서드
    public void SetGroundBounds(Bounds bounds)
    {
        groundBounds = bounds;
        hasGroundBounds = true;
    }
    
    private void Update()
    {
        if (!isServer) return;
        
        if (isStunned) return;
        
        // 플레이어 감지 및 추적
        DetectAndChasePlayer();
        
        // 일반 이동 로직
        if (currentTarget == null && Time.time > nextPositionChangeTime)
        {
            ChooseNewPosition();
            nextPositionChangeTime = Time.time + positionChangeInterval;
        }
        
        // 이동 로직
        MoveToTarget();
    }
    
    // 플레이어 감지 및 추적 로직
    [Server]
    private void DetectAndChasePlayer()
    {
        // 현재 타겟이 없거나 타겟이 파괴되었다면
        if (currentTarget == null || currentTarget.activeSelf == false)
        {
            // 주변 플레이어 탐색
            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, detectionRadius);
            
            float closestDistance = float.MaxValue;
            GameObject closestPlayer = null;
            
            foreach (Collider2D collider in colliders)
            {
                if (collider.CompareTag("Player"))
                {
                    float distance = Vector3.Distance(transform.position, collider.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPlayer = collider.gameObject;
                    }
                }
            }
            
            currentTarget = closestPlayer;
        }
        
        // 타겟이 있으면 타겟 위치로 이동
        if (currentTarget != null)
        {
            targetPosition = currentTarget.transform.position;
        }
    }
    
    // 이동 로직
    [Server]
    private void MoveToTarget()
    {
        // 목표 지점까지의 방향 계산
        Vector3 direction = (targetPosition - transform.position).normalized;
        
        // 이동 속도 계산
        Vector3 movement = direction * moveSpeed * Time.deltaTime;
        
        // 다음 위치 계산
        Vector3 nextPosition = transform.position + movement;
        
        // 그라운드 경계 내에 있는지 확인
        if (hasGroundBounds)
        {
            // 경계 내로 제한
            float borderMargin = 0.5f;
            nextPosition.x = Mathf.Clamp(nextPosition.x, groundBounds.min.x + borderMargin, groundBounds.max.x - borderMargin);
            nextPosition.y = Mathf.Clamp(nextPosition.y, groundBounds.min.y + borderMargin, groundBounds.max.y - borderMargin);
        }
        
        // 이동 적용
        transform.position = nextPosition;
        
        // 방향에 따라 스프라이트 뒤집기
        if (direction.x < 0)
        {
            spriteRenderer.flipX = true;
        }
        else if (direction.x > 0)
        {
            spriteRenderer.flipX = false;
        }
    }
    
    // 새로운 이동 위치 선택
    [Server]
    private void ChooseNewPosition()
    {
        float randomX = Random.Range(-5f, 5f);
        float randomY = Random.Range(-5f, 5f);

        if (!hasGroundBounds)
        {
            // 그라운드 경계가 없으면 현재 위치 주변으로 이동
            targetPosition = transform.position + new Vector3(randomX, randomY, 0);
            return;
        }
        
        // 그라운드 경계 내에서 랜덤 위치 선택
        float borderMargin = 1.0f;
        randomX = Random.Range(groundBounds.min.x + borderMargin, groundBounds.max.x - borderMargin);
        randomY = Random.Range(groundBounds.min.y + borderMargin, groundBounds.max.y - borderMargin);
        
        targetPosition = new Vector3(randomX, randomY, 0);
    }
    
    // 데미지 처리
    [Server]
    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        
        // 스턴 처리
        isStunned = true;
        monsterColor = Color.red; // SyncVar를 통해 모든 클라이언트에 전파됨

        StartCoroutine(ResetStunState());
        
        // 사망 처리
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    // 스턴 상태 리셋
    [Server]
    private IEnumerator ResetStunState()
    {
        yield return new WaitForSeconds(stunDuration);

        isStunned = false;
        monsterColor = Color.white; // SyncVar를 통해 모든 클라이언트에 전파됨
    }
    
    // 사망 처리
    [Server]
    private void Die()
    {
        // 충돌 비활성화
        if (GetComponent<Collider2D>() != null)
        {
            GetComponent<Collider2D>().enabled = false;
        }
        
        // 오브젝트 제거
        NetworkServer.Destroy(gameObject);
    }
    
    // 체력 변경 시 호출되는 메서드
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        // 체력 UI 업데이트 등의 작업 수행
    }
}
