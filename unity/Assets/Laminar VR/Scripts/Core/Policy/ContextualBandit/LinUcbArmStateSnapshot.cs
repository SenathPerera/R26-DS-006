using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class LinUcbArmStateSnapshot
    {
        private readonly double[,] designMatrix;
        private readonly double[] rewardVector;

        public LinUcbArmStateSnapshot(
            EnvironmentAction action,
            double[,] designMatrix,
            double[] rewardVector,
            long updateCount)
        {
            if (designMatrix == null)
            {
                throw new ArgumentNullException(nameof(designMatrix));
            }

            if (rewardVector == null)
            {
                throw new ArgumentNullException(nameof(rewardVector));
            }

            Action = action;
            this.designMatrix = (double[,])designMatrix.Clone();
            this.rewardVector = (double[])rewardVector.Clone();
            UpdateCount = updateCount;
        }

        public EnvironmentAction Action { get; }

        public int FeatureCount => rewardVector.Length;

        public long UpdateCount { get; }

        public double GetDesignMatrixValue(int row, int column)
        {
            return designMatrix[row, column];
        }

        public double GetRewardVectorValue(int index)
        {
            return rewardVector[index];
        }

        public double[,] CopyDesignMatrix()
        {
            return (double[,])designMatrix.Clone();
        }

        public double[] CopyRewardVector()
        {
            return (double[])rewardVector.Clone();
        }
    }
}
