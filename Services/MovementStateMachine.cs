using ArtaleAI.Utils;
using System;
using System.Collections.Generic;

namespace ArtaleAI.Services
{
    public enum MovementState
    {
        Idle,
        MovingHorizontal,
        MovingVertical,
        ClimbingRope,
        Jumping,
        Attacking
    }

    /// <summary>移動狀態與每狀態最小停留時間（冷卻），供輸入節流。</summary>
    public class MovementStateMachine
    {
        private MovementState _currentState = MovementState.Idle;
        private DateTime _stateEnterTime = DateTime.UtcNow;
        private readonly object _stateLock = new object();
        
        private readonly Dictionary<MovementState, int> _stateCooldowns = new()
        {
            { MovementState.Idle, 0 },
            { MovementState.MovingHorizontal, 20 },
            { MovementState.MovingVertical, 20 },
            { MovementState.Jumping, 20 },
            { MovementState.Attacking, 20 }
        };

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

        public int GetCooldownForCurrentState()
        {
            lock (_stateLock)
            {
                return _stateCooldowns.TryGetValue(_currentState, out var cooldown) ? cooldown : 0;
            }
        }

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

        public bool CanTransition()
        {
            lock (_stateLock)
            {
                return RemainingCooldown <= 0;
            }
        }

        /// <summary>若冷卻已過則切換狀態；已在目標狀態時回傳 true。</summary>
        public bool TryTransition(MovementState newState)
        {
            lock (_stateLock)
            {
                if (_currentState == newState) return true;

                if (_stateCooldowns.TryGetValue(_currentState, out var cooldown))
                {
                    if (TimeInCurrentState < cooldown)
                    {
                        Logger.Debug($"[狀態機] 冷卻中: {_currentState} (剩餘 {cooldown - TimeInCurrentState}ms)");
                        return false;
                    }
                }

                var oldState = _currentState;
                _currentState = newState;
                _stateEnterTime = DateTime.UtcNow;
                Logger.Debug($"[狀態機] {oldState} → {newState}");
                return true;
            }
        }

        /// <summary>略過冷卻立即切換（例如邊界／重置情境）。</summary>
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

        public void Reset()
        {
            lock (_stateLock)
            {
                _currentState = MovementState.Idle;
                _stateEnterTime = DateTime.UtcNow;
                Logger.Debug("[狀態機] 重置為 Idle");
            }
        }

        public bool CanPerformAction(MovementState requiredState)
        {
            lock (_stateLock)
            {
                return _currentState == MovementState.Idle || _currentState == requiredState;
            }
        }

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

        public void SetCooldown(MovementState state, int cooldownMs)
        {
            lock (_stateLock)
            {
                _stateCooldowns[state] = cooldownMs;
            }
        }

        public string GetStatusSummary()
        {
            lock (_stateLock)
            {
                return $"State={_currentState}, TimeInState={TimeInCurrentState}ms, Remaining={RemainingCooldown}ms";
            }
        }
    }
}
