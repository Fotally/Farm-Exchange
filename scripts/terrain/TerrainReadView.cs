using Godot;

namespace FarmExchange.Terrain;

/**
 * <summary>共享本局权威地形的读取能力，不授予道路维护或资源释放能力。</summary>
 * <remarks>不复制地形或道路状态；局拥有者完成导航准备后才能查询新路线。</remarks>
 */
public sealed class TerrainReadView
{
    private readonly TerrainMap _map;

    internal TerrainReadView(TerrainMap map) => _map = map;

    /**
     * <summary>本局独立地形种子。</summary>
     */
    public int Seed => _map.Seed;

    /**
     * <summary>本局是否为显式纯陆地。</summary>
     */
    public bool IsAllLand => _map.IsAllLand;

    /**
     * <summary>读取基础格四角的权威地形。</summary>
     * <param name="cell">地图内基础格；越界抛参数异常。</param>
     * <returns>按顶、右、底、左排列的水、土或草。</returns>
     */
    public TerrainCellSnapshot GetCell(Vector2I cell) => _map.GetCell(cell);

    /**
     * <summary>检查基础格是否全部为陆地。</summary>
     * <param name="cell">基础格坐标。</param>
     * <returns>四角无水时为 true；含水或地图外为 false。</returns>
     */
    public bool IsFullLand(Vector2I cell) => _map.IsFullLand(cell);

    /**
     * <summary>查询保持真实连续起终点的完整圆脚底路线。</summary>
     * <param name="start">基础格平面的真实连续起点。</param>
     * <param name="target">真实目标，不吸附或移动目标。</param>
     * <returns>完整不可变路线或明确拒绝，不返回部分路线。</returns>
     * <remarks>原生导航同步期间或局释放后抛异常；不推进准备或重新创建资源。</remarks>
     */
    public LandPathResult FindLandPath(LandPosition start, LandPosition target) => _map.FindLandPath(start, target);
}
