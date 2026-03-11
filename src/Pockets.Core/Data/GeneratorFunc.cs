using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// A pipeline generator function. Receives the previous pipeline value (null if first step)
/// and any resolved template arguments from the step's inline (@template-id) references.
/// Returns the next pipeline value.
/// </summary>
public delegate PipelineValue GeneratorFunc(PipelineValue? input, IReadOnlyList<object> templateArgs);
