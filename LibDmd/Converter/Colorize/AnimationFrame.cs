using System.Collections.Generic;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class AnimationFrame
	{
		/// <summary>
		/// Duration of the frame
		/// </summary>
		protected int Delay;

		public List<AnimationPlane> Planes { get; protected set; }
		public bool HasMask { get; protected set; }

		protected byte[] Hash;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	}
}