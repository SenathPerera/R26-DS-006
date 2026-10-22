using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public readonly struct LinUcbSensitivityCell
    {
        public LinUcbSensitivityCell(
            string code,
            double explorationCoefficient,
            double ridgeRegularization,
            bool isProductionCell)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            ExplorationCoefficient = explorationCoefficient;
            RidgeRegularization = ridgeRegularization;
            IsProductionCell = isProductionCell;
        }

        public string Code { get; }
        public double ExplorationCoefficient { get; }
        public double RidgeRegularization { get; }
        public bool IsProductionCell { get; }
        public string ExperimentId => "study-c-" + Code;
        public string ConfigurationId =>
            "adaptive-vr-linucb-sensitivity-" + Code + "-v1";
    }

    public static class LinUcbSensitivityCatalog
    {
        public const double ProductionExplorationCoefficient = 0.25d;
        public const double ProductionRidgeRegularization = 1d;

        private static readonly LinUcbSensitivityCell[] Cells =
        {
            Cell("a0p00-r0p10", 0d, 0.1d),
            Cell("a0p00-r1p00", 0d, 1d),
            Cell("a0p00-r10p00", 0d, 10d),
            Cell("a0p10-r0p10", 0.1d, 0.1d),
            Cell("a0p10-r1p00", 0.1d, 1d),
            Cell("a0p10-r10p00", 0.1d, 10d),
            Cell("a0p25-r0p10", 0.25d, 0.1d),
            Cell("a0p25-r1p00", 0.25d, 1d, true),
            Cell("a0p25-r10p00", 0.25d, 10d),
            Cell("a0p50-r0p10", 0.5d, 0.1d),
            Cell("a0p50-r1p00", 0.5d, 1d),
            Cell("a0p50-r10p00", 0.5d, 10d)
        };

        public static int Count => Cells.Length;

        public static LinUcbSensitivityCell GetAt(int index)
        {
            if (index < 0 || index >= Cells.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Cells[index];
        }

        public static bool Contains(LinUcbSensitivityCell candidate)
        {
            for (var index = 0; index < Cells.Length; index++)
            {
                var cell = Cells[index];
                if (string.Equals(cell.Code, candidate.Code, StringComparison.Ordinal)
                    && cell.ExplorationCoefficient
                        == candidate.ExplorationCoefficient
                    && cell.RidgeRegularization
                        == candidate.RidgeRegularization
                    && cell.IsProductionCell == candidate.IsProductionCell)
                {
                    return true;
                }
            }

            return false;
        }

        public static LinUcbModelConfiguration CreateConfiguration(
            LinUcbSensitivityCell cell,
            LinUcbModelConfiguration productionConfiguration)
        {
            if (!Contains(cell))
            {
                throw new ArgumentException(
                    "The LinUCB sensitivity cell is not in the frozen grid.",
                    nameof(cell));
            }

            if (productionConfiguration == null)
            {
                throw new ArgumentNullException(nameof(productionConfiguration));
            }

            return new LinUcbModelConfiguration(
                cell.ConfigurationId,
                1,
                productionConfiguration.FeatureSchemaVersion,
                productionConfiguration.FeatureCount,
                cell.RidgeRegularization,
                cell.ExplorationCoefficient);
        }

        private static LinUcbSensitivityCell Cell(
            string code,
            double explorationCoefficient,
            double ridgeRegularization,
            bool production = false)
        {
            return new LinUcbSensitivityCell(
                code,
                explorationCoefficient,
                ridgeRegularization,
                production);
        }
    }
}
