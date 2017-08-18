using System.Collections.Generic;
using NLog;

namespace LibDmd.Converter.Colorize
{
	public abstract class AnimationSet
	{
		public int Version { get; protected set; }
		public List<Animation> Animations { get; protected set; }

		protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public override string ToString()
		{
			return $"VPIN v{Version}, {Animations.Count} animation(s)";
		}
	}
}