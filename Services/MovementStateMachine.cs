using ArtaleAI.Utils;
using System;
using System.Collections.Generic;

namespace ArtaleAI.Services
{
    /// <summary>
    /// 移動狀態列舉
    /// </summary>
    public enum MovementState
    {
        Idle,              // 待機
        MovingHorizontal,  // 水平移動
        MovingVertical,    // 垂直移動 (如下跳、爬梯)
        ClimbingRope,      // 爬繩
        Jumping,           // 跳躍（未來擴展）
        Attacking          // 攻擊（未來擴展）
    }

    /// <summary>
    /// 移動狀態機 - 管理角色的移動狀態和冷卻時間
    /// </summary>
    public class MovementStateMachine
    {
        private MovementState _currentState = MovementState.Idle;
        private DateTime _stateEnterTime = DateTime.UtcNow;
        private readonly object _stateLock = new object();
        
        /// <summary>
        /// 各狀態的冷卻時間（毫秒）
        /// </summary>
        private readonly Dictionary<MovementState, int> _stateCooldowns = new()
        {
            { MovementState.Idle, 0 },
            { MovementState.MovingHorizontal, 20 }, // 縮減至微型同步緩衝
            { MovementState.MovingVertical, 20 },
            { MovementState.Jumping, 20 },
            { MovementState.Attacking, 20 }
        };

        /// <summary>
        /// 取得當前狀態
        /// </summary>
        public MovementState CurrentState 
        { 
            get 
            { 
                lock (_stateLock) 
                { 
                    return _currentState; 
                } 
            } 
        }

        /// <summary>
        /// 取得在當前狀態中的時間（毫秒）
        /// </summary>
        public int TimeInCurrentState 
        { 
            get 
            { 
                lock (_stateLock) 
                { 
                    return (int)(DateTime.UtcNow - _stateEnterTime).TotalMilliseconds; 
                } 
            } 
        }

        /// <summary>
        /// 取得當前狀態的冷卻時間（毫秒）
        /// </summary>
        public int GetCooldownForCurrentState()
        {
            lock (_stateLock)
            {
                return _stateCooldowns.TryGetValue(_currentState, out var cooldown) ? cooldown : 0;
            }
        }

        /// <summary>
        /// 取得剩餘冷卻時間（毫秒）
        /// </summary>
        public int RemainingCooldown
        {
            get
            {
                lock (_stateLock)
                {
                    int cooldown = GetCooldownForCurrentState();
                    int elapsed = TimeInCurrentState;
                    return Math.Max(0, cooldown - elapsed);
                }
            }
        }

        /// <summary>
        /// 檢查是否可以切換狀態（冷卻時間已過）
        /// </summary>
        public bool CanTransition()
        {
            lock (_stateLock)
            {
                return RemainingCooldown <= 0;
            }
        }

        /// <summary>
        /// 嘗試切換到新狀態（會檢查冷卻時間）
        /// </summary>
        /// <param name="newState">新狀態</param>
        /// <returns>是否成功切換</returns>
        public bool TryTransition(MovementState newState)
        {
            lock (_stateLock)
            {
                // 如果已經是目標狀態，直接返回 true
                if (_currentState == newState) return true;
                
                // 檢查是否需要冷卻
                if (_stateCooldowns.TryGetValue(_currentState, out var cooldown))
                {
                    if (TimeInCurrentState < cooldown)
                    {
                        Logger.Debug($"[狀態機] 冷卻中: {_currentState} (剩餘 {cooldown - TimeInCurrentState}ms)");
                        return false;
                    }
                }
                
                // 切換狀態
                var oldState = _currentState;
                _currentState = newState;
                _stateEnterTime = DateTime.UtcNow;
                Logger.Debug($"[狀態機] {oldState} → {newState}");
                return true;
            }
        }

        /// <summary>
        /// 強制切換狀態（緊急情況，如碰到邊界）
        /// </summary>
        /// <param name="newState">新狀態</param>
        public void ForceTransition(MovementState newState)
        {
            lock (_stateLock)
            {
                if (_currentState == newState) return;
                
                var oldState = _currentState;
                _currentState = newState;
                _stateEnterTime = DateTime.UtcNow;
                Logger.Warning($"[狀態機] 強制切換: {oldState} → {newState}");
            }
        }

        /// <summary>
        /// 重置到待機狀態
        /// </summary>
        public void Reset()
        {
            lock (_stateLock)
            {
                _currentState = MovementState.Idle;
                _stateEnterTime = DateTime.UtcNow;
                Logger.Debug("[狀態機] 重置為 Idle");
            }
        }

        /// <summary>
        /// 檢查是否可以執行指定動作
        /// </summary>
        /// <param name="requiredState">需要的狀態</param>
        /// <returns>是否可以執行</returns>
        public bool CanPerformAction(MovementState requiredState)
        {
            lock (_stateLock)
            {
                // 如果是待機狀態或已經在執行相同動作，可以執行
                return _currentState == MovementState.Idle || _currentState == requiredState;
            }
        }

        /// <summary>
        /// 檢查是否正在執行任何動作（非待機狀態）
        /// </summary>
        public bool IsPerformingAction
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentState != MovementState.Idle;
                }
            }
        }

        /// <summary>
        /// 設定特定狀態的冷卻時間
        /// </summary>
        /// <param name="state">狀態</param>
        /// <param name="cooldownMs">冷卻時間（毫秒）</param>
        public void SetCooldown(MovementState state, int cooldownMs)
        {
            lock (_stateLock)
            {
                _stateCooldowns[state] = cooldownMs;
            }
        }

        /// <summary>
        /// 取得狀態機狀態摘要（用於調試）
        /// </summary>
        public string GetStatusSummary()
        {
            lock (_stateLock)
            {
                return $"State={_currentState}, TimeInState={TimeInCurrentState}ms, Remaining={RemainingCooldown}ms";
            }
        }
    }
}
