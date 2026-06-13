namespace DisplaySelector.Core.Audio;

/// <summary>A live audio output endpoint. <see cref="Id"/> is the Core Audio endpoint string (stable across reboots).</summary>
public sealed record AudioEndpoint(string Id, string FriendlyName, bool IsDefault);
