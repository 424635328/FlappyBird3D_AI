using UnityEngine;

public class PipeMovement : MonoBehaviour
{
    // --- 移除或注释掉自己的速度变量 ---
    // public float speed = 3.5f;

    // --- 保留销毁位置变量 ---
    [Tooltip("管道移动到此 X 坐标左侧时将被销毁。")]
    public float destroyXPosition = -10f;

    // --- 可以不需要 Start 方法了，除非有其他初始化 ---
    // void Start() { }

    void Update()
    {
        // --- 获取当前速度 ---
        // 调用辅助方法从 GameManager 获取速度，包含安全检查
        float speedToUse = GetCurrentSpeedFromManager();

        // --- 使用获取到的速度进行移动 ---
        // 将管道对向左移动
        transform.position += Vector3.left * speedToUse * Time.deltaTime;

        // --- 检查销毁条件 (逻辑不变) ---
        // 如果管道移出屏幕左侧，则销毁自身
        if (transform.position.x < destroyXPosition)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 从 GameManager 单例安全地获取当前的管道速度。
    /// </summary>
    /// <returns>当前的管道速度；如果 GameManager 不存在，则返回 0。</returns>
    private float GetCurrentSpeedFromManager()
    {
        // 检查 GameManager 的静态实例是否存在
        if (GameManager.Instance != null)
        {
            // 如果存在，返回其 currentPipeSpeed 属性
            return GameManager.Instance.currentPipeSpeed;
        }
        else
        {
            // 如果 GameManager 实例不存在（可能发生在场景加载/卸载的瞬间，或未正确设置）
            // 记录一个警告，并返回一个安全的值（例如 0，使管道停止，更容易发现问题）
            if (!Application.isPlaying) return 0f; // 编辑器模式下非运行状态不报警告
            Debug.LogWarning($"GameManager.Instance 未找到！无法获取管道速度。管道将停止移动。 ({gameObject.name})", gameObject);
            return 0f; // 返回 0
        }
    }
}