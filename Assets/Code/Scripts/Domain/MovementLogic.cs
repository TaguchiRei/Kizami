using UnityEngine;

namespace UsefulVr.Domain.Runtime.Domain
{
    /// <summary>
    /// 重力方向に基づいた移動ベクトルの計算を行う純粋なロジッククラス
    /// </summary>
    public static class MovementLogic
    {
        /// <summary>
        /// 現在の速度から重力方向の速度成分を抽出する
        /// </summary>
        public static Vector3 GetGravityVelocity(
            Vector3 velocity,
            Vector3 gravityDirection)
        {
            Vector3 gravity = gravityDirection.normalized;

            return Vector3.Dot(velocity, gravity) * gravity;
        }

        /// <summary>
        /// 現在の速度から接平面方向の速度成分を抽出する
        /// </summary>
        public static Vector3 GetTangentVelocity(
            Vector3 velocity,
            Vector3 gravityDirection)
        {
            return velocity -
                   GetGravityVelocity(
                       velocity,
                       gravityDirection);
        }

        /// <summary>
        /// 重力方向に対して垂直な前方向を算出する
        /// </summary>
        public static Vector3 GetTangentForward(
            Vector3 gravityDirection)
        {
            Vector3 gravity = gravityDirection.normalized;

            Vector3 fallback =
                Mathf.Abs(Vector3.Dot(gravity, Vector3.forward)) < 0.99f
                    ? Vector3.forward
                    : Vector3.right;

            return Vector3.ProjectOnPlane(
                fallback,
                gravity).normalized;
        }

        /// <summary>
        /// 入力と視線方向、重力方向に基づいた移動ベクトルを計算する
        /// </summary>
        public static Vector3 CalculateMoveVector(
            Vector2 input,
            Vector3 gravityDirection,
            Vector3 lookForward)
        {
            if (input.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            Vector3 gravity = gravityDirection.normalized;

            // 視線方向を接平面へ投影
            Vector3 tangentForward =
                Vector3.ProjectOnPlane(
                    lookForward,
                    gravity);

            if (tangentForward.sqrMagnitude < 0.001f)
            {
                tangentForward =
                    GetTangentForward(gravity);
            }

            tangentForward.Normalize();

            // Right生成
            Vector3 tangentRight =
                Vector3.Cross(
                    tangentForward,
                    gravity).normalized;

            // 入力方向生成
            Vector3 moveDirection =
                tangentRight * input.x +
                tangentForward * input.y;

            if (moveDirection.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            return moveDirection.normalized *
                   input.magnitude;
        }

        /// <summary>
        /// 移動入力停止時、自分が加えた移動成分のみ除去する
        /// </summary>
        public static Vector3 CalculateVelocityAfterStop(
            Vector3 currentVelocity,
            Vector3 movePower)
        {
            if (movePower.sqrMagnitude < 0.001f)
            {
                return currentVelocity;
            }

            Vector3 moveDir = movePower.normalized;

            float currentSpeedOnMoveDir =
                Vector3.Dot(currentVelocity, moveDir);

            float speedToRemove =
                Mathf.Clamp(
                    currentSpeedOnMoveDir,
                    0f,
                    movePower.magnitude);

            return currentVelocity -
                   moveDir * speedToRemove;
        }
    }
}