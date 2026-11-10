using UnityEngine;

public class BirdController : MonoBehaviour
{
    public float jumpForce = 5f; // 跳跃力度
    private Rigidbody rb;
    public bool isAlive = true; // 游戏状态标志

    void Awake() // 使用 Awake 确保 Rigidbody 在 Start 或 Update 之前被获取
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("Rigidbody component not found on the Bird!", gameObject);
        }
    }

    // Update 每帧调用 - 用于手动输入测试
    void Update()
    {
        // 如果 Agent 存活 并且 按下了空格键 (GetKeyDown 只在按下的那一帧触发)
        if (isAlive && Input.GetKeyDown(KeyCode.Space))
        {
            Jump(); // 调用跳跃方法
        }
        if (transform.position.y < -5f || transform.position.y > 5f)
        {
             // 不过这里的 Die() 不会结束 ML-Agents 的回合
             Die();
        }
    }

    // 跳跃方法
    public void Jump()
    {
        if (rb == null) return; // 检查 Rigidbody 是否存在
        if (!isAlive) return; // 如果死亡则不跳跃

        // 重置垂直速度以获得一致跳跃
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        // 施加向上的力
        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
    }

    // 碰撞检测
    void OnCollisionEnter(Collision collision)
    {
        // 如果存活且与障碍物碰撞
        if (isAlive && collision.gameObject.CompareTag("Obstacle"))
        {
            Die();
        }
    }

    // 死亡方法
    public void Die()
    {
         if (!isAlive) return; // 防止重复调用

         isAlive = false;
         Debug.Log("小鸟死亡! (from BirdController)"); // 标识来源
    }

    // 重置小鸟状态的方法 
    public void ResetBird(Vector3 startPosition)
    {
        if (rb == null)
        {
            Debug.LogError("ResetBird called but Rigidbody (rb) is null!", gameObject);
            rb = GetComponent<Rigidbody>();
            if (rb == null) return;
        }

        transform.position = startPosition; // 重置位置
        rb.velocity = Vector3.zero; // 重置速度
        rb.angularVelocity = Vector3.zero; // 重置角速度
        isAlive = true; // 重置存活状态
    }
}