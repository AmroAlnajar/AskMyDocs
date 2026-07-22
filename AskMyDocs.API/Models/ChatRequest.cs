using System.ComponentModel.DataAnnotations;

namespace AskMyDocs.API.Models;

public record ChatRequest(
	[property: Required(AllowEmptyStrings = false)]
	string Message);
