namespace IronHive.Host.Server;

/// <summary>
/// Why <see cref="HitlBridge.ResolveElicitation(IronHive.Host.Protocol.ElicitationResponseRequest, out ElicitationRefusal?)"/>
/// refused an answer to a form: the question it was for and the first field of the answer that breaks the form's schema.
/// </summary>
/// <param name="Id">The <see cref="IronHive.Host.Protocol.ElicitationRequestEvent.Id"/> of the question, which is still open.</param>
/// <param name="Problem">One sentence naming the field (no value the person entered).</param>
public sealed record ElicitationRefusal(string Id, string Problem);
