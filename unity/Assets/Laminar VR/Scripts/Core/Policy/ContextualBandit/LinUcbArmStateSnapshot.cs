using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class LinUcbArmStateSnapshot
    {
        private readonly double[,] designMatrix;
        private readonly double[] rewardVector;

        internal LinUcbArmStateSnapshot(
            EnvironmentAction action,
            double[,] designMatrix,
            double[] rewardVector,
            long updateCount)
        {
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
