using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class SimpleHeuristicAgent : Agent
{
    // 用于在Inspector中方便查看计数
    public int heuristicCallCount = 0;
    public int actionReceivedCallCount = 0;

    // 初始化或重置Agent时调用
    public override void OnEpisodeBegin()
    {
        heuristicCallCount = 0;
        actionReceivedCallCount = 0;
        Debug.Log("--- New Episode Starting ---");
    }

    // 收集观察信息（此测试中可以为空）
    public override void CollectObservations(VectorSensor sensor)
    {
        // 对于这个简单的测试，我们不需要任何观察
        // sensor.AddObservation(...);
    }

    // 接收动作并执行（在Heuristic模式下，这里的actions通常来自Heuristic方法）
    public override void OnActionReceived(ActionBuffers actions)
    {
        actionReceivedCallCount++;
        // 可选：打印收到的来自Heuristic的模拟动作
        // Debug.Log($"Action Received: {actions.ContinuousActions[0]}");
    }

    // !!! 核心测试方法 !!!
    // 当Behavior Type设置为Heuristic Only时，此方法应该被调用以生成动作
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        heuristicCallCount++;
        Debug.Log($"Heuristic() method called! Count: {heuristicCallCount}");

        // 模拟玩家输入 - 例如，读取水平轴输入
        float moveX = Input.GetAxis("Horizontal"); // 使用键盘左右箭头或A/D键

        // 将模拟输入写入动作缓冲区
        // 注意：需要与Behavior Parameters中定义的Action Space匹配
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = moveX;

        // 如果你使用离散动作空间：
        // ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        // discreteActions[0] = someDiscreteAction; // 基于输入计算出的离散动作
    }
}