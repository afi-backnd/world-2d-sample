using UnityEngine;
using BACKND;
using System.Collections.Generic;
using System.Linq;

public class Meteor : NetworkBehaviour
{
    [Header("Meteor Settings")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float meteorSpreadRadius = 1.5f;
    [SerializeField] private LayerMask targetLayers;
    
    [Header("Animation")]
    [SerializeField] private float flyingDuration = 0.29f; // 날아가는 애니메이션 (0~29초)
    [SerializeField] private float totalAnimationDuration = 0.56f; // 전체 애니메이션 길이
        
    private Vector3 startPosition;
    private bool isInitialized = false;
    private float timer = 0f;
    private Rigidbody2D rb;
    private bool hasExploded = false;
    
    // 충돌 감지된 오브젝트 저장용 리스트
    private HashSet<GameObject> detectedObjects = new HashSet<GameObject>();
    
    // 서버에서 메테오 초기화
    [Server]
    public void Initialize(Vector3 start)
    {
        startPosition = start;
        transform.position = startPosition;
        isInitialized = true;

        rb = GetComponent<Rigidbody2D>();
    }
    
    private void FixedUpdate()
    {
        if (!isServer || !isInitialized) return;
        
        // 타이머 업데이트
        timer += Time.fixedUnscaledDeltaTime;
        
        // 날아가는 단계 (0~29초)
        if (timer <= flyingDuration)
        {
            // 왼쪽에서 오른쪽으로 대각선 아래로 애니메이션 시간에 따라 이동
            float t = timer / flyingDuration;
            Vector3 targetPosition = startPosition + new Vector3(2, -2, 0) * meteorSpreadRadius;
            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
        }
        // 폭발 시점에 도달 (29초)
        else if (timer >= flyingDuration && !hasExploded)
        {
            // 폭발 처리
            Explode();
            hasExploded = true;
        }
        // 애니메이션 완료 후 파괴
        else if (timer >= totalAnimationDuration)
        {
            NetworkServer.Destroy(gameObject);
        }
    }
    
    // Trigger 충돌 감지 (서버에서만 처리)
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isServer) return;
        
        // 타겟 레이어에 해당하는지 확인
        if (((1 << other.gameObject.layer) & targetLayers) != 0)
        {
            // 감지된 오브젝트 저장
            detectedObjects.Add(other.gameObject);
        }
    }
    
    // 트리거에서 벗어난 경우
    private void OnTriggerExit2D(Collider2D other)
    {
        if (!isServer) return;
        
        // 목록에서 제거
        detectedObjects.Remove(other.gameObject);
    }
    
    [Server]
    private void Explode()
    {       
        GameObject[] objectsToProcess = detectedObjects.ToArray();
        
        foreach (GameObject hitObject in objectsToProcess)
        {
            if (hitObject != null) // null 체크 추가
            {
                Monster monster = hitObject.GetComponent<Monster>();
                if (monster != null)
                {
                    monster.TakeDamage((int)damage);
                }
            }
        }
    }
}
