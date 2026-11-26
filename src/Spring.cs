using System;

namespace BlindCover
{
    // 자연스러운 움직임의 기본: 목표를 향해 살짝 지나쳤다가 통 하고 멈추는 스프링
    class Spring
    {
        public double X, V, Target;
        public double K, D;

        public Spring(double x, double stiffness, double dampingRatio)
        {
            X = Target = x;
            Set(stiffness, dampingRatio);
        }

        public void Set(double stiffness, double dampingRatio)
        {
            K = stiffness;
            D = 2 * Math.Sqrt(stiffness) * dampingRatio;
        }

        public void Snap(double x) { X = Target = x; V = 0; }

        public void Step(double dt)
        {
            // 작은 단계로 나눠 계산해서 프레임이 들쭉날쭉해도 안정적으로
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = -K * (X - Target) - D * V;
                V += a * h;
                X += V * h;
            }
        }

        public bool Resting(double eps)
        {
            return Math.Abs(X - Target) < eps && Math.Abs(V) < eps * 10;
        }
    }
}
