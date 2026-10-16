using System;
using System.Numerics;

namespace FarmExchange.Time;

/**
 * <summary>按绝对经营秒索引一年窗口内的预约，并按同秒登记顺序读取到期项。</summary>
 * <remarks>只拥有预约；调用方完成业务后取消句柄。读取到期项会封闭当前秒登记。</remarks>
 */
public sealed class SimulationDueIndex
{
    /**
     * <summary>预约相对当前水位的最大秒数，包含恰好一年。</summary>
     */
    public const int MaxDelaySeconds = GameCalendar.DaysPerYear * GameTimeUnits.PerDay / GameTimeUnits.PerSecond;
    private const int BucketCount = 32768;
    private const int BucketMask = BucketCount - 1;
    private const int FreeEntry = -2;
    private readonly int[] _heads = new int[BucketCount];
    private readonly int[] _tails = new int[BucketCount];
    private readonly ulong[] _leaves = new ulong[BucketCount / 64];
    private readonly ulong[] _summaries = new ulong[BucketCount / 64 / 64];
    private readonly Entry[] _entries;
    private int _freeHead;
    private bool _currentSecondClosed;

    private struct Entry
    {
        public long DueSecond;
        public ulong Generation;
        public int Anchor;
        public int Previous;
        public int Next;
    }

    /**
     * <summary>只用于原索引的取消凭据；记录位置复用或清空后旧凭据失效。</summary>
     */
    public readonly record struct Handle
    {
        internal SimulationDueIndex Owner { get; }
        internal int Position { get; }
        internal ulong Generation { get; }

        internal Handle(SimulationDueIndex owner, int position, ulong generation)
        {
            Owner = owner;
            Position = position;
            Generation = generation;
        }
    }

    /**
     * <summary>创建独立容量的到期索引，初始秒允许登记。</summary>
     * <param name="capacity">同时挂起预约的正整数上限，与秒桶数无关。</param>
     * <param name="currentSecond">非负绝对经营秒。</param>
     */
    public SimulationDueIndex(int capacity, long currentSecond = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(currentSecond);
        _entries = new Entry[capacity];
        CurrentSecond = currentSecond;
        Clear();
    }

    /**
     * <summary>当前仍挂起的预约数量。</summary>
     */
    public int Count { get; private set; }

    /**
     * <summary>同时挂起预约的固定上限。</summary>
     */
    public int Capacity => _entries.Length;

    /**
     * <summary>当前绝对经营秒水位。</summary>
     */
    public long CurrentSecond { get; private set; }

    /**
     * <summary>在目标秒尾部登记预约。</summary>
     * <param name="dueSecond">当前未封口秒至未来一年内的绝对经营秒。</param>
     * <param name="anchor">业务自行解释的整数锚点，不参与排序。</param>
     * <returns>本次预约的取消凭据。</returns>
     * <remarks>期限非法抛 ArgumentOutOfRangeException，容量不足抛 InvalidOperationException；失败零修改。</remarks>
     */
    public Handle Register(long dueSecond, int anchor)
    {
        if (dueSecond < CurrentSecond || dueSecond - CurrentSecond > MaxDelaySeconds ||
            (dueSecond == CurrentSecond && _currentSecondClosed))
            throw new ArgumentOutOfRangeException(nameof(dueSecond), "预约必须位于当前未封口秒至未来一年内。");
        if (_freeHead < 0)
            throw new InvalidOperationException("到期索引预约容量已满。");

        int position = _freeHead;
        ref Entry entry = ref _entries[position];
        ulong generation = checked(entry.Generation + 1);
        int bucket = (int)(dueSecond & BucketMask);
        int tail = _tails[bucket];
        _freeHead = entry.Next;
        entry.DueSecond = dueSecond;
        entry.Generation = generation;
        entry.Anchor = anchor;
        entry.Previous = tail;
        entry.Next = -1;
        if (tail >= 0)
            _entries[tail].Next = position;
        else
        {
            _heads[bucket] = position;
            _leaves[bucket >> 6] |= 1UL << (bucket & 63);
            _summaries[bucket >> 12] |= 1UL << ((bucket >> 6) & 63);
        }
        _tails[bucket] = position;
        Count++;
        return new Handle(this, position, generation);
    }

    /**
     * <summary>摘除指定预约并立即归还记录位置。</summary>
     * <param name="handle">登记或到期读取返回的凭据。</param>
     * <returns>仍有效时返回 true；默认、过期、重复或其他索引的凭据返回 false。</returns>
     */
    public bool Cancel(Handle handle)
    {
        if (!ReferenceEquals(handle.Owner, this))
            return false;
        ref Entry entry = ref _entries[handle.Position];
        if (entry.Previous == FreeEntry || entry.Generation != handle.Generation)
            return false;

        int bucket = (int)(entry.DueSecond & BucketMask);
        if (entry.Previous >= 0)
            _entries[entry.Previous].Next = entry.Next;
        else
            _heads[bucket] = entry.Next;
        if (entry.Next >= 0)
            _entries[entry.Next].Previous = entry.Previous;
        else
            _tails[bucket] = entry.Previous;
        if (_heads[bucket] < 0)
        {
            int leaf = bucket >> 6;
            _leaves[leaf] &= ~(1UL << (bucket & 63));
            if (_leaves[leaf] == 0)
                _summaries[leaf >> 6] &= ~(1UL << (leaf & 63));
        }
        entry.Previous = FreeEntry;
        entry.Next = _freeHead;
        _freeHead = handle.Position;
        Count--;
        return true;
    }

    /**
     * <summary>查询最近仍挂起的绝对到期秒，不改变水位或封口状态。</summary>
     * <param name="dueSecond">成功时为最近到期秒，空索引时为零。</param>
     * <returns>存在预约时返回 true。</returns>
     */
    public bool TryGetNext(out long dueSecond)
    {
        if (Count == 0)
        {
            dueSecond = 0;
            return false;
        }
        int bucket = FindBucketAtOrAfter((int)(CurrentSecond & BucketMask));
        if (bucket < 0)
            bucket = FindBucketAtOrAfter(0);
        dueSecond = _entries[_heads[bucket]].DueSecond;
        return true;
    }

    /**
     * <summary>将水位推进到尚未越过最早挂起预约的绝对经营秒。</summary>
     * <param name="currentSecond">不得小于当前水位；允许到达最早预约秒。</param>
     * <remarks>倒退或越过预约均失败零修改；重复进入同秒不重新开放登记。</remarks>
     */
    public void AdvanceTo(long currentSecond)
    {
        if (currentSecond < CurrentSecond)
            throw new ArgumentOutOfRangeException(nameof(currentSecond), "到期索引水位不得倒退。");
        if (currentSecond == CurrentSecond)
            return;
        if (TryGetNext(out long nextSecond) && currentSecond > nextSecond)
            throw new InvalidOperationException("不得越过尚未消费的到期预约。");
        CurrentSecond = currentSecond;
        _currentSecondClosed = false;
    }

    /**
     * <summary>封闭当前秒登记并读取本秒最早预约，不移除预约。</summary>
     * <param name="handle">成功时为队首凭据，否则为默认值。</param>
     * <param name="anchor">成功时为队首业务锚点，否则为零。</param>
     * <returns>当前秒存在挂起预约时返回 true。</returns>
     * <remarks>空秒同样封口。业务成功后调用 Cancel；业务异常时当前及后续预约保持挂起，不自动重试。</remarks>
     */
    public bool TryPeekDue(out Handle handle, out int anchor)
    {
        _currentSecondClosed = true;
        int position = _heads[(int)(CurrentSecond & BucketMask)];
        if (position < 0)
        {
            handle = default;
            anchor = 0;
            return false;
        }
        ref Entry entry = ref _entries[position];
        handle = new Handle(this, position, entry.Generation);
        anchor = entry.Anchor;
        return true;
    }

    /**
     * <summary>撤销全部预约并复用现有存储。</summary>
     * <remarks>保留当前水位及封口状态；旧句柄失效。新经营局应新建索引。</remarks>
     */
    public void Clear()
    {
        Array.Fill(_heads, -1);
        Array.Fill(_tails, -1);
        Array.Clear(_leaves);
        Array.Clear(_summaries);
        for (int position = 0; position < _entries.Length; position++)
        {
            _entries[position].Previous = FreeEntry;
            _entries[position].Next = position + 1 < _entries.Length ? position + 1 : -1;
        }
        _freeHead = 0;
        Count = 0;
    }

    private int FindBucketAtOrAfter(int start)
    {
        int leaf = start >> 6;
        ulong bits = _leaves[leaf] & (ulong.MaxValue << (start & 63));
        if (bits != 0)
            return leaf * 64 + BitOperations.TrailingZeroCount(bits);

        int nextLeaf = leaf + 1;
        for (int summary = nextLeaf >> 6; summary < _summaries.Length; summary++)
        {
            ulong leaves = _summaries[summary];
            if (summary == nextLeaf >> 6)
                leaves &= ulong.MaxValue << (nextLeaf & 63);
            if (leaves == 0)
                continue;
            int foundLeaf = summary * 64 + BitOperations.TrailingZeroCount(leaves);
            return foundLeaf * 64 + BitOperations.TrailingZeroCount(_leaves[foundLeaf]);
        }
        return -1;
    }
}
