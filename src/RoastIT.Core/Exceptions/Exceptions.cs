namespace RoastIT.Core.Exceptions;

// Typed exceptions the services throw (plan §11). Components map them to inline errors or toasts.
// Messages are safe to show to the user and never contain a PIN, hash or recovery code.

/// <summary>Input failed a rule. The message is the inline error text.</summary>
public class ValidationException(string message) : Exception(message);

/// <summary>The app is locked (or not set up), so the action is refused.</summary>
public class AccessDeniedException(string message = "RoastIT is locked.") : Exception(message);

/// <summary>A critical action ran without a valid, unexpired PIN confirmation.</summary>
public class PinRequiredException(string message = "Enter your PIN to continue.") : Exception(message);

/// <summary>The data changed under the caller (stale screen, double tap).</summary>
public class StaleStateException(string message) : Exception(message);
