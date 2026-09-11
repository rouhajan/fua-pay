using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FuaPay.Web.Pages.Shared;

[HtmlTargetElement("div", Attributes = AttributeName)]
public sealed class CspValidationSummaryTagHelper : TagHelper
{
    private const string AttributeName = "fua-validation-summary";
    private readonly IHtmlGenerator _generator;

    public CspValidationSummaryTagHelper(IHtmlGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
    }

    [HtmlAttributeName(AttributeName)]
    public ValidationSummary Mode { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.Attributes.RemoveAll(AttributeName);

        if (Mode != ValidationSummary.All)
        {
            throw new InvalidOperationException(
                "The FUA Pay validation summary supports All mode only.");
        }

        if (
            !ViewContext.ClientValidationEnabled &&
            ViewContext.ViewData.ModelState.IsValid)
        {
            output.SuppressOutput();
            return;
        }

        if (!HasRenderableError())
        {
            RenderEmptySummary(output);
            return;
        }

        var generatedSummary = _generator.GenerateValidationSummary(
            ViewContext,
            excludePropertyErrors: false,
            message: null,
            headerTag: null,
            htmlAttributes: null);

        if (generatedSummary is null)
        {
            output.SuppressOutput();
            return;
        }

        output.MergeAttributes(generatedSummary);
        if (generatedSummary.HasInnerHtml)
        {
            output.PostContent.AppendHtml(
                generatedSummary.InnerHtml);
        }
    }

    private bool HasRenderableError()
    {
        return ViewContext.ViewData.ModelState.Values.Any(
            modelState => modelState.Errors.Any(
                error => !string.IsNullOrEmpty(error.ErrorMessage)));
    }

    private void RenderEmptySummary(TagHelperOutput output)
    {
        var summary = new TagBuilder("div");
        summary.AddCssClass(
            ViewContext.ViewData.ModelState.IsValid
                ? HtmlHelper.ValidationSummaryValidCssClassName
                : HtmlHelper.ValidationSummaryCssClassName);

        if (ViewContext.ClientValidationEnabled)
        {
            summary.MergeAttribute(
                "data-valmsg-summary",
                "true");
        }

        output.MergeAttributes(summary);
        output.Content.SetHtmlContent(new TagBuilder("ul"));
    }
}
