using System;
using System.Threading;

namespace ArtaleAI.Domain.Input
{
    /// <summary>
    /// 鍵盤輸入租約：同一時刻僅一個 Owner 可送鍵。
    /// 優先序（可強佔 Combat）：Party／ChangeChannel／FarmDismiss；彼此不可互搶。
    /// </summary>
    public sealed class InputLease
    {
        private int _owner;

        public InputOwner Current => (InputOwner)Volatile.Read(ref _owner);

        public bool IsIdle => Current == InputOwner.Idle;

        public bool IsHeldBy(InputOwner owner) => Current == owner;

        /// <summary>方向鍵應讓路：任一獨佔 Owner 持有時。</summary>
        public bool BlocksNavigationKeys => !IsIdle;

        /// <summary>不可新開攻擊：租約非 Idle。</summary>
        public bool BlocksCombatStart => !IsIdle;

        /// <summary>僅從 Idle 取得；已是自己則視為成功（冪等）。</summary>
        public bool TryAcquire(InputOwner owner)
        {
            if (owner == InputOwner.Idle)
                throw new ArgumentOutOfRangeException(nameof(owner));

            int desired = (int)owner;
            while (true)
            {
                int current = Volatile.Read(ref _owner);
                if (current == desired)
                    return true;

                if (current != (int)InputOwner.Idle)
                    return false;

                if (Interlocked.CompareExchange(ref _owner, desired, (int)InputOwner.Idle)
                    == (int)InputOwner.Idle)
                    return true;
            }
        }

        /// <summary>
        /// UI 序列專用：可從 Idle 取得，或強佔 Combat。
        /// 強佔成功時呼叫 <paramref name="onCombatPreempted"/>（應放鍵，勿再動租約）。
        /// </summary>
        public bool TryAcquirePreemptingCombat(InputOwner owner, Action? onCombatPreempted = null)
        {
            if (owner is not (InputOwner.Party or InputOwner.ChangeChannel or InputOwner.FarmDismiss))
                throw new ArgumentOutOfRangeException(nameof(owner));

            int desired = (int)owner;
            while (true)
            {
                int current = Volatile.Read(ref _owner);
                if (current == desired)
                    return true;

                if (current == (int)InputOwner.Idle)
                {
                    if (Interlocked.CompareExchange(ref _owner, desired, (int)InputOwner.Idle)
                        == (int)InputOwner.Idle)
                        return true;
                    continue;
                }

                if (current == (int)InputOwner.Combat)
                {
                    if (Interlocked.CompareExchange(ref _owner, desired, (int)InputOwner.Combat)
                        != (int)InputOwner.Combat)
                        continue;

                    onCombatPreempted?.Invoke();
                    return true;
                }

                return false;
            }
        }

        /// <summary>僅當目前為指定 Owner 時釋放回 Idle。</summary>
        public void Release(InputOwner owner)
        {
            if (owner == InputOwner.Idle)
                return;

            Interlocked.CompareExchange(ref _owner, (int)InputOwner.Idle, (int)owner);
        }

        /// <summary>導航搶鍵：強制釋放 Combat（不影響 Party／UI 序列）。</summary>
        public void PreemptCombat()
        {
            Interlocked.CompareExchange(
                ref _owner,
                (int)InputOwner.Idle,
                (int)InputOwner.Combat);
        }
    }
}
