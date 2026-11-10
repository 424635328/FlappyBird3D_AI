// ScoreTrigger.cs
using UnityEngine;

public class ScoreTrigger : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // 确保是标记为 "Player" 的物体（即小鸟）首次触发
        if (other.gameObject.CompareTag("Player"))
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.BirdScored(); // 通知 GameManager 计分
            }
            // 立即禁用碰撞体，防止同一对管道因物理帧等原因重复计分
            GetComponent<Collider>().enabled = false;
        }
    }

    // 当管道对被重置或重新生成时，需要重新启用碰撞体
    // 可以由 PipeSpawner 在 Instantiate 后调用，或者在 PipeMovement 的 OnEnable 中处理
    void OnEnable()
    {
         // 确保每次启用时触发器都是可用的
         var col = GetComponent<Collider>();
         if(col != null) col.enabled = true;
    }
}