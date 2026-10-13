using System;
using System.Collections.Generic;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class AudioBanditArmSnapshot
    {
        public double[] covariance;
        public double[] rewardVector;
        public int observations;
    }

    [Serializable]
    public sealed class AudioBanditSnapshot
    {
        public int version = 1;
        public string ownerKey;
        public string featureSchema;
        public string actionSchema;
        public string configurationKey;
        public int featureCount;
        public double alpha;
        public double ridge;
        public AudioBanditArmSnapshot[] arms;
    }

    public readonly struct AudioBanditSelection
    {
        public AudioBanditSelection(int arm, double mean, double uncertainty, double score)
        {
            Arm = arm;
            Mean = mean;
            Uncertainty = uncertainty;
            Score = score;
        }

        public int Arm { get; }
        public double Mean { get; }
        public double Uncertainty { get; }
        public double Score { get; }
    }

    // Disjoint LinUCB: A <- A + xx', b <- b + reward*x, once per valid outcome.
    // Cholesky solves avoid explicit matrix inversion and reject corrupt snapshots.
    public sealed class AudioLinUcbModel
    {
        private AudioBanditArmSnapshot[] arms;
        public int FeatureCount { get; }
        public int ArmCount => arms.Length;
        public double Alpha { get; }
        public double Ridge { get; }
        public int UpdateCount
        {
            get
            {
                int count = 0;
                foreach (var arm in arms) count = checked(count + arm.observations);
                return count;
            }
        }

        public AudioLinUcbModel(int featureCount, int armCount, double alpha, double ridge)
        {
            if (featureCount < 1 || featureCount > 128 || armCount < 1 || armCount > 64
                || !IsFinite(alpha) || alpha < 0 || !IsFinite(ridge) || ridge <= 0)
                throw new ArgumentOutOfRangeException(nameof(featureCount));
            FeatureCount = featureCount;
            Alpha = alpha;
            Ridge = ridge;
            arms = new AudioBanditArmSnapshot[armCount];
            for (int a = 0; a < armCount; a++)
            {
                arms[a] = new AudioBanditArmSnapshot
                {
                    covariance = new double[featureCount * featureCount],
                    rewardVector = new double[featureCount]
                };
                for (int i = 0; i < featureCount; i++) arms[a].covariance[i * featureCount + i] = ridge;
            }
        }

        public AudioBanditSelection Select(double[] features, IReadOnlyList<int> eligibleArms, bool explore = true)
        {
            ValidateFeatures(features);
            if (eligibleArms == null || eligibleArms.Count == 0)
                throw new ArgumentException("At least NoChange must be eligible.", nameof(eligibleArms));
            var best = new AudioBanditSelection(-1, 0, 0, double.NegativeInfinity);
            foreach (int arm in eligibleArms)
            {
                ValidateArm(arm);
                double[] lower = Factor(arms[arm].covariance, FeatureCount);
                double[] theta = Solve(lower, arms[arm].rewardVector, FeatureCount);
                double[] projected = Solve(lower, features, FeatureCount);
                double mean = Dot(features, theta);
                double uncertainty = Math.Sqrt(Math.Max(0, Dot(features, projected)));
                double score = mean + (explore ? Alpha * uncertainty : 0d);
                if (!IsFinite(score)) throw new InvalidOperationException("Non-finite bandit score.");
                // All nonzero catalogue actions have equal size. Arm zero is NoChange.
                if (score > best.Score + 1e-10 || (Math.Abs(score - best.Score) <= 1e-10 && arm < best.Arm))
                    best = new AudioBanditSelection(arm, mean, uncertainty, score);
            }
            return best;
        }

        public void Observe(int executedArm, double[] features, double reward)
        {
            ValidateArm(executedArm);
            ValidateFeatures(features);
            if (!IsFinite(reward) || Math.Abs(reward) > 3)
                throw new ArgumentOutOfRangeException(nameof(reward));
            var arm = Clone(arms[executedArm]);
            for (int i = 0; i < FeatureCount; i++)
            {
                arm.rewardVector[i] += reward * features[i];
                for (int j = 0; j < FeatureCount; j++)
                    arm.covariance[i * FeatureCount + j] += features[i] * features[j];
            }
            arm.observations = checked(arm.observations + 1);
            ValidateSnapshotArm(arm);
            arms[executedArm] = arm;
        }

        public AudioBanditSnapshot Capture()
        {
            var snapshot = new AudioBanditSnapshot
            {
                featureCount = FeatureCount, alpha = Alpha, ridge = Ridge,
                arms = new AudioBanditArmSnapshot[ArmCount]
            };
            for (int a = 0; a < ArmCount; a++) snapshot.arms[a] = Clone(arms[a]);
            return snapshot;
        }

        public bool TryRestore(AudioBanditSnapshot snapshot, out string error)
        {
            try
            {
                if (snapshot == null || snapshot.version != 1 || snapshot.featureCount != FeatureCount
                    || snapshot.arms == null || snapshot.arms.Length != ArmCount
                    || snapshot.alpha != Alpha || snapshot.ridge != Ridge)
                    throw new ArgumentException("Incompatible bandit dimensions or configuration.");
                var restored = new AudioBanditArmSnapshot[ArmCount];
                int observations = 0;
                foreach (var arm in snapshot.arms)
                {
                    ValidateSnapshotArm(arm);
                    observations = checked(observations + arm.observations);
                }
                for (int a = 0; a < ArmCount; a++) restored[a] = Clone(snapshot.arms[a]);
                // Commit only after the entire snapshot has passed validation.
                arms = restored;
                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            {
                error = ex.Message;
                return false;
            }
        }

        private void ValidateSnapshotArm(AudioBanditArmSnapshot arm)
        {
            if (arm == null || arm.observations < 0 || arm.covariance == null || arm.rewardVector == null
                || arm.covariance.Length != FeatureCount * FeatureCount || arm.rewardVector.Length != FeatureCount)
                throw new ArgumentException("Invalid arm dimensions or sample count.");
            for (int i = 0; i < FeatureCount; i++)
            {
                if (!IsFinite(arm.rewardVector[i])) throw new ArgumentException("Invalid reward vector.");
                for (int j = 0; j < FeatureCount; j++)
                {
                    double entry = arm.covariance[i * FeatureCount + j];
                    if (!IsFinite(entry) || Math.Abs(entry - arm.covariance[j * FeatureCount + i]) > 1e-8)
                        throw new ArgumentException("Covariance must be finite and symmetric.");
                }
            }
            Factor(arm.covariance, FeatureCount);
        }

        private void ValidateFeatures(double[] features)
        {
            if (features == null || features.Length != FeatureCount) throw new ArgumentException("Feature schema mismatch.");
            foreach (double value in features)
                if (!IsFinite(value) || Math.Abs(value) > 1.000001) throw new ArgumentException("Features must be finite and bounded.");
        }

        private void ValidateArm(int arm)
        {
            if (arm < 0 || arm >= ArmCount) throw new ArgumentOutOfRangeException(nameof(arm));
        }

        private static AudioBanditArmSnapshot Clone(AudioBanditArmSnapshot arm) => new AudioBanditArmSnapshot
        {
            covariance = (double[])arm.covariance.Clone(), rewardVector = (double[])arm.rewardVector.Clone(),
            observations = arm.observations
        };

        private static double[] Factor(double[] matrix, int n)
        {
            var lower = new double[n * n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = matrix[i * n + j];
                for (int k = 0; k < j; k++) sum -= lower[i * n + k] * lower[j * n + k];
                if (i == j)
                {
                    if (!IsFinite(sum) || sum <= 1e-12) throw new InvalidOperationException("Covariance is not positive definite.");
                    lower[i * n + j] = Math.Sqrt(sum);
                }
                else lower[i * n + j] = sum / lower[j * n + j];
            }
            return lower;
        }

        private static double[] Solve(double[] lower, double[] rhs, int n)
        {
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = rhs[i];
                for (int j = 0; j < i; j++) sum -= lower[i * n + j] * result[j];
                result[i] = sum / lower[i * n + i];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = result[i];
                for (int j = i + 1; j < n; j++) sum -= lower[j * n + i] * result[j];
                result[i] = sum / lower[i * n + i];
            }
            return result;
        }

        private static double Dot(double[] a, double[] b)
        {
            double value = 0;
            for (int i = 0; i < a.Length; i++) value += a[i] * b[i];
            return value;
        }

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
