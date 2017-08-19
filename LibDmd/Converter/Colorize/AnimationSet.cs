using System.Collections.Generic;
using System.Linq;
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

		public Animation Find(uint offset)
		{
			return Animations.FirstOrDefault(animation => animation.Offset == offset);
		}
	}
}