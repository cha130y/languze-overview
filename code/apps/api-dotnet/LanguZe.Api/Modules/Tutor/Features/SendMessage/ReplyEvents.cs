namespace LanguZe.Api.Modules.Tutor.Features.SendMessage;

/// <summary>A piece of the reply, as the browser's <c>delta</c> event carries it (API design 3.6).</summary>
internal sealed record DeltaEvent(string Text);

/// <summary>The reply is whole and saved, with the day's messages left (<c>done</c>).</summary>
internal sealed record DoneEvent(Guid MessageId, Guid ReplyId, int MessagesLeft);

/// <summary>The reply failed once the stream was open (<c>error</c>); nothing was saved, and the message was given back.</summary>
internal sealed record ErrorEvent(string Code);
