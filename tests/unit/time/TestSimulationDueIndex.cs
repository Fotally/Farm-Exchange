using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Time;

public partial class TestSimulationDueIndex : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("经营到期索引契约检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks()
    {
        try
        {
            CheckFifoAndCancellation();
            CheckClosingAndFailureRetention();
            CheckLimitsAndWatermark();
            CheckBoundariesAndReuse();
            CheckReferenceSequences();
            return true;
        }
        catch (Exception error)
        {
            GD.PushError($"经营到期索引：{error}");
            return false;
        }
    }

    private static void CheckFifoAndCancellation()
    {
        var index = new SimulationDueIndex(6);
        var head = index.Register(52, 90);
        var middle = index.Register(52, 30);
        var survivor = index.Register(52, 70);
        var tail = index.Register(52, 10);
        Require(index.Cancel(middle) && index.Cancel(head) && index.Cancel(tail), "头中尾取消失败");
        var appended = index.Register(52, 20);
        Require(!index.Cancel(tail) && !index.Cancel(default), "槽位复用后旧句柄或默认句柄仍可取消");
        Require(index.Count == 2 && index.Capacity == 6, "取消未立即释放记录容量");
        index.AdvanceTo(52);
        ReadAndCancel(index, survivor, 70);
        ReadAndCancel(index, appended, 20);
        Require(!index.TryGetNext(out _) && !index.TryPeekDue(out _, out _), "末项取消后索引未清空");

        var another = new SimulationDueIndex(1, 52);
        var otherHandle = another.Register(53, 1);
        Require(!index.Cancel(otherHandle) && another.Count == 1, "跨索引句柄误取消");
        var first = index.Register(100, 1);
        var second = index.Register(200, 2);
        Require(index.Cancel(first) && index.TryGetNext(out long next) && next == 200,
            "取消最早项后最近查询仍指向旧秒");
        index.AdvanceTo(200);
        ReadAndCancel(index, second, 2);
    }

    private static void CheckClosingAndFailureRetention()
    {
        var index = new SimulationDueIndex(3);
        var first = index.Register(0, 8);
        var second = index.Register(0, 3);
        Require(index.TryGetNext(out long next) && next == 0, "当前秒预约没有进入最近查询");
        var third = index.Register(0, 5);
        Require(index.TryPeekDue(out var peeked, out int anchor) && peeked == first && anchor == 8,
            "读取没有返回登记队首");
        Expect<ArgumentOutOfRangeException>(() => index.Register(0, 99));
        index.AdvanceTo(0);
        Expect<ArgumentOutOfRangeException>(() => index.Register(0, 99));
        Expect<InvalidOperationException>(() => index.AdvanceTo(1));

        try
        {
            throw new InvalidOperationException("模拟业务容量异常");
        }
        catch (InvalidOperationException)
        {
            Require(index.Count == 3 && index.TryPeekDue(out var retained, out int retainedAnchor) &&
                retained == first && retainedAnchor == 8, "业务异常连带移除了当前或后续预约");
        }
        ReadAndCancel(index, first, 8);
        ReadAndCancel(index, second, 3);
        ReadAndCancel(index, third, 5);
        index.Clear();
        Expect<ArgumentOutOfRangeException>(() => index.Register(0, 99));
        index.AdvanceTo(1);
        Require(!index.TryPeekDue(out _, out _), "空秒出现到期项");
        Expect<ArgumentOutOfRangeException>(() => index.Register(1, 99));
        index.AdvanceTo(1);
        Expect<ArgumentOutOfRangeException>(() => index.Register(1, 99));
        index.AdvanceTo(2);
        var reopened = index.Register(2, 7);
        ReadAndCancel(index, reopened, 7);
    }

    private static void CheckLimitsAndWatermark()
    {
        Expect<ArgumentOutOfRangeException>(() => new SimulationDueIndex(0));
        Expect<ArgumentOutOfRangeException>(() => new SimulationDueIndex(-1));
        Expect<ArgumentOutOfRangeException>(() => new SimulationDueIndex(1, -1));
        var index = new SimulationDueIndex(2, 100);
        var first = index.Register(101, 1);
        var year = index.Register(100 + SimulationDueIndex.MaxDelaySeconds, 2);
        Expect<InvalidOperationException>(() => index.Register(102, 3));
        Expect<ArgumentOutOfRangeException>(() => index.Register(99, 3));
        Expect<ArgumentOutOfRangeException>(() => index.Register(101 + SimulationDueIndex.MaxDelaySeconds, 3));
        Expect<ArgumentOutOfRangeException>(() => index.AdvanceTo(99));
        Expect<InvalidOperationException>(() => index.AdvanceTo(102));
        Require(index.Count == 2 && index.CurrentSecond == 100 && index.TryGetNext(out long next) && next == 101,
            "容量、期限或推进失败修改了原状态");
        index.AdvanceTo(101);
        ReadAndCancel(index, first, 1);
        index.AdvanceTo(100 + SimulationDueIndex.MaxDelaySeconds);
        ReadAndCancel(index, year, 2);
        index.AdvanceTo(1000000000000);
        Require(index.CurrentSecond == 1000000000000, "空轮不能跨多年直接推进");
        var nearMaximum = new SimulationDueIndex(1, long.MaxValue - 1);
        var last = nearMaximum.Register(long.MaxValue, 9);
        Expect<ArgumentOutOfRangeException>(() => nearMaximum.Register(long.MinValue, 10));
        nearMaximum.AdvanceTo(long.MaxValue);
        ReadAndCancel(nearMaximum, last, 9);
    }

    private static void CheckBoundariesAndReuse()
    {
        var index = new SimulationDueIndex(4);
        var at51 = index.Register(51, 1);
        var exact361 = index.Register(GameTimeUnits.RemainingSeconds(361), 361);
        var exact358 = index.Register(GameTimeUnits.RemainingSeconds(358), 358);
        index.AdvanceTo(51);
        ReadAndCancel(index, at51, 1);
        Require(!index.TryPeekDue(out _, out _), "第51秒提前交出第52秒事件");
        index.AdvanceTo(52);
        ReadAndCancel(index, exact361, 361);
        ReadAndCancel(index, exact358, 358);

        index.AdvanceTo(32760);
        var afterRing = index.Register(32769, 2);
        var beforeRing = index.Register(32767, 1);
        Require(index.TryGetNext(out long next) && next == 32767, "跨环查询未优先当前环末端");
        index.AdvanceTo(32767);
        ReadAndCancel(index, beforeRing, 1);
        Require(index.TryGetNext(out next) && next == 32769, "跨环查询未找到下一环开端");
        index.AdvanceTo(32769);
        ReadAndCancel(index, afterRing, 2);

        index.AdvanceTo(uint.MaxValue);
        var beyondCalendar = index.Register((long)uint.MaxValue + 1, 3);
        index.Clear();
        Require(index.CurrentSecond == uint.MaxValue && index.Count == 0 && !index.Cancel(beyondCalendar),
            "清空改变水位或旧凭据仍有效");
        var reused = index.Register((long)uint.MaxValue + 1, 4);
        Require(!index.Cancel(beyondCalendar), "清空后复用槽位重新接受旧凭据");
        index.AdvanceTo((long)uint.MaxValue + 1);
        ReadAndCancel(index, reused, 4);
        index.Clear();
        Require(!index.Cancel(reused), "已消费凭据在清空后恢复有效");
    }

    private static void CheckReferenceSequences()
    {
        var random = new Random(65065);
        var index = new SimulationDueIndex(128);
        long second = 0;
        int sequence = 0;
        for (int round = 0; round < 80; round++)
        {
            index.AdvanceTo(second);
            var reference = new List<Reservation>();
            for (int item = 0; item < 100; item++)
            {
                // 集中同秒与稀疏全年混合；参考结构独立按时间和登记号排序。
                long due = second + (item % 2 == 0 ? 64 : random.Next(SimulationDueIndex.MaxDelaySeconds + 1));
                int anchor = random.Next(1000);
                reference.Add(new Reservation(due, sequence++, anchor, index.Register(due, anchor)));
            }
            for (int item = reference.Count - 1; item >= 0; item -= 3)
            {
                Reservation removed = reference[item];
                Require(index.Cancel(removed.Handle), "随机序列取消失败");
                reference.RemoveAt(item);
                if (item % 2 == 0)
                    reference.Add(new Reservation(removed.Due, sequence++, removed.Anchor,
                        index.Register(removed.Due, removed.Anchor)));
                Require(!index.Cancel(removed.Handle), "随机序列复用命中旧代次");
            }
            reference.Sort((left, right) => left.Due != right.Due
                ? left.Due.CompareTo(right.Due) : left.Sequence.CompareTo(right.Sequence));
            Require(index.Count == reference.Count, "随机序列有效数量不一致");
            foreach (Reservation expected in reference)
            {
                Require(index.TryGetNext(out long due) && due == expected.Due, "独立参考最近秒不一致");
                index.AdvanceTo(expected.Due);
                ReadAndCancel(index, expected.Handle, expected.Anchor);
            }
            Require(index.Count == 0 && !index.TryGetNext(out _), "随机序列漏消费预约");
            second += SimulationDueIndex.MaxDelaySeconds + random.Next(1, 10000);
        }
    }

    private readonly record struct Reservation(long Due, int Sequence, int Anchor, SimulationDueIndex.Handle Handle);

    private static void ReadAndCancel(SimulationDueIndex index, SimulationDueIndex.Handle expected, int anchor)
    {
        Require(index.TryPeekDue(out var actual, out int actualAnchor) && actual == expected && actualAnchor == anchor,
            "同秒FIFO、锚点或预约凭据与预期不符");
        Require(index.TryPeekDue(out var repeated, out _) && repeated == expected, "读取意外摘除了队首");
        Require(index.Cancel(actual) && !index.Cancel(actual), "成功消费未释放预约或重复消费成功");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"预期 {typeof(T).Name} 未抛出。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
