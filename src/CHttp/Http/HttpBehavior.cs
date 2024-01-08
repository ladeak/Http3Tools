namespace CHttp;

internal record HttpBehavior(
    double Timeout,
    bool ToUtf8,
    string CookieContainer,
    SocketBehavior SocketsBehavior);
