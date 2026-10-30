import { HttpInterceptorFn } from '@angular/common/http';

const sessionKey = 'shameen.tabSession';

export const tabSessionInterceptor: HttpInterceptorFn = (request, next) => {
  const token = sessionStorage.getItem(sessionKey);
  if (!token || request.url.endsWith('/api/auth/login') || request.url.endsWith('/api/auth/setup') || request.url.endsWith('/api/auth/status'))
    return next(request);
  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
