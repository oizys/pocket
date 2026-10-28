using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// A pipeline generator function. Receives the previous pipeline value (null if first step)
/// and any resolved template arguments from the step's inline (@template-id) references, plus the
/// RNG the pipeline runs under (the step's Craft stream in play, the seeded init RNG at world build).
/// A generator draws only from <paramref name="rng"/>. Returns the next pipeline value.
/// </summary>
public delegate PipelineValue GeneratorFunc(PipelineValue? input, IReadOnlyList<object> templateArgs, Random rng);
