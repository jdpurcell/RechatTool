using System;
using System.Threading;
using System.Threading.Tasks;

namespace RechatTool;

internal static class AsyncHelper {
	public static T RunSync<T>(Func<Task<T>> getTask) {
		SynchronizationContext originalContext = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(null);
		try {
			return getTask().GetAwaiter().GetResult();
		}
		finally {
			SynchronizationContext.SetSynchronizationContext(originalContext);
		}
	}
}
