namespace AskMyDocs.API.Services.Documents;

internal static class MarkdownChunker
{
	public const int ChunkSize = 500;
	public const int Overlap = 100;

	private static readonly string[] Separators = ["\n\n", "\n", ". ", "? ", "! ", " "];

	public static List<DocumentChunk> Split(string content, string source)
	{
		if (string.IsNullOrWhiteSpace(content))
			return [];

		var chunks = new List<DocumentChunk>();
		var index = 0;

		foreach (var (heading, body) in SplitSections(content))
		{
			foreach (var piece in ChunkBody(body))
			{
				chunks.Add(new DocumentChunk(piece, source, heading, index++));
			}
		}

		return chunks;
	}

	private static List<(string Heading, string Body)> SplitSections(string content)
	{
		content = content.Replace("\r\n", "\n");
		var sections = new List<(string Heading, string Body)>();
		var heading = "";
		var body = new System.Text.StringBuilder();

		void Flush()
		{
			var text = body.ToString().Trim();
			body.Clear();

			if (text.Length == 0)
				return;

			sections.Add((heading, text));
		}

		foreach (var line in content.Split('\n'))
		{
			if (TryParseHeading(line, out var next))
			{
				Flush();
				heading = next;
				continue;
			}

			if (body.Length > 0)
				body.Append('\n');

			body.Append(line);
		}

		Flush();
		return sections;
	}

	private static bool TryParseHeading(string line, out string heading)
	{
		heading = "";

		if (line.Length < 3 || line[0] != '#')
			return false;

		var level = 0;
		while (level < line.Length && level < 6 && line[level] == '#')
			level++;

		if (level >= line.Length || line[level] != ' ')
			return false;

		heading = line[(level + 1)..].Trim();
		return heading.Length > 0;
	}

	private static List<string> ChunkBody(string text)
		=> Merge(SplitRecursive(text.Trim(), Separators));

	private static List<string> SplitRecursive(string text, string[] separators)
	{
		if (text.Length == 0)
			return [];

		if (text.Length <= ChunkSize)
			return [text];

		if (separators.Length == 0)
			return HardSplit(text);

		var sep = separators[0];
		var rest = separators[1..];

		if (!text.Contains(sep, StringComparison.Ordinal))
			return SplitRecursive(text, rest);

		var raw = text.Split(sep);
		var pieces = new List<string>();

		for (var i = 0; i < raw.Length; i++)
		{
			var piece = i < raw.Length - 1 ? raw[i] + sep : raw[i];
			if (piece.Length == 0)
				continue;

			if (piece.Length <= ChunkSize)
				pieces.Add(piece);
			else
				pieces.AddRange(SplitRecursive(piece, rest));
		}

		return pieces;
	}

	private static List<string> HardSplit(string text)
	{
		var pieces = new List<string>();

		for (var i = 0; i < text.Length; i += ChunkSize)
		{
			var length = Math.Min(ChunkSize, text.Length - i);
			pieces.Add(text.Substring(i, length));
		}

		return pieces;
	}

	private static List<string> Merge(List<string> pieces)
	{
		if (pieces.Count == 0)
			return [];

		var chunks = new List<string>();
		var current = new System.Text.StringBuilder();

		foreach (var piece in pieces)
		{
			if (current.Length > 0 && current.Length + piece.Length > ChunkSize)
			{
				var flushed = current.ToString().Trim();
				if (flushed.Length > 0)
					chunks.Add(flushed);

				current.Clear();

				var overlap = TakeOverlap(flushed);
				if (overlap.Length > 0 && overlap.Length + piece.Length <= ChunkSize)
					AppendPiece(current, overlap);
			}

			AppendPiece(current, piece);
		}

		var last = current.ToString().Trim();
		if (last.Length > 0)
			chunks.Add(last);

		return chunks;
	}

	private static string TakeOverlap(string text)
	{
		if (text.Length <= Overlap)
			return text;

		var slice = text[^Overlap..];
		var cut = slice.IndexOfAny([' ', '\n']);
		if (cut >= 0 && cut < slice.Length - 1)
			slice = slice[(cut + 1)..];

		return slice.Trim();
	}

	private static void AppendPiece(System.Text.StringBuilder current, string piece)
	{
		if (current.Length == 0)
		{
			current.Append(piece);
			return;
		}

		if (!char.IsWhiteSpace(current[^1]) && !char.IsWhiteSpace(piece[0]))
			current.Append("\n\n");

		current.Append(piece);
	}
}
