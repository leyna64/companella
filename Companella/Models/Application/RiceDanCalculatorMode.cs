namespace Companella.Models.Application;

/// <summary>
/// Selects which rice dan calculator Companella uses for 4K maps.
/// </summary>
public enum RiceDanCalculatorMode
{
	/// <summary>
	/// Companella ONNX model (MSD + Interlude + Sunny).
	/// </summary>
	CompanellaOnnx = 0,

	/// <summary>
	/// Daniel rice algorithm. Falls back to ONNX below Alpha threshold.
	/// </summary>
	Daniel = 1
}
