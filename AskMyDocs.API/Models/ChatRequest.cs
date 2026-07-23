using System.ComponentModel.DataAnnotations;

namespace AskMyDocs.API.Models;

public record ChatRequest(
	[Required(AllowEmptyStrings = false)]
	string Message);
