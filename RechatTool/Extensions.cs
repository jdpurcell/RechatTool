// --------------------------------------------------------------------------------
// Copyright (c) J.D. Purcell
//
// Licensed under the MIT License (see LICENSE.txt)
// --------------------------------------------------------------------------------
using System;

namespace RechatTool;

public static class Extensions {
	public static string NullIfEmpty(this string s) {
		return String.IsNullOrEmpty(s) ? null : s;
	}

	public static int? TryParseInt32(this string s) {
		return Int32.TryParse(s, out int n) ? n : null;
	}

	public static long? TryParseInt64(this string s) {
		return Int64.TryParse(s, out long n) ? n : null;
	}

	public static string ToDisplayString(this Version v) {
		return $"{v.Major}.{v.Minor}.{v.Revision}";
	}
}
