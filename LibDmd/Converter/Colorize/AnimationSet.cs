using System.Collections.Generic;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class AnimationSet
	{
		protected int Version;
		protected List<Animation> Animations;

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public override string ToString()
		{
			return $"VPIN v{Version}, {Animations.Count} animation(s)";
		}
	}
}