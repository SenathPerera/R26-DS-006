using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationStudyExecutionCoordinator
    {
        public async Task<EvaluationArtifactReceipt> RunAsync(
            EvaluationStudyId study,
            EvaluationRuntimeBundle bundle,
            string evaluationRoot,
            string runId,
            EvaluationRunProvenance provenance,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            using (var writer = new EvaluationArtifactWriter(
                evaluationRoot,
                runId,
                bundle,
                provenance))
            {
                EvaluationStudyResult result;
                try
                {
                    var runner = new EvaluationExperimentRunner();
                    switch (study)
                    {
                        case EvaluationStudyId.StudyAPrimaryPolicyComparison:
                            result = await runner.RunStudyAToSinkAsync(
                                bundle,
                                writer,
                                progress,
                                cancellationToken).ConfigureAwait(false);
                            break;
                        case EvaluationStudyId.StudyBRobustness:
                            result = await runner.RunStudyBToSinkAsync(
                                bundle,
                                writer,
                                progress,
                                cancellationToken).ConfigureAwait(false);
                            break;
                        case EvaluationStudyId.StudyCLinUcbSensitivity:
                            result = await runner.RunStudyCToSinkAsync(
                                bundle,
                                writer,
                                progress,
                                cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(study));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    writer.Abort(
                        study,
                        "cancelled",
                        "The run was cancelled before all declared sessions completed.",
                        DateTimeOffset.UtcNow);
                    throw;
                }
                catch (Exception exception)
                {
                    writer.Abort(
                        study,
                        "failed",
                        exception.GetType().Name + ": " + exception.Message,
                        DateTimeOffset.UtcNow);
                    throw;
                }
                return writer.Complete(result, DateTimeOffset.UtcNow);
            }
        }
    }
}
