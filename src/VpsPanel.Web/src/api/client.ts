export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

async function toError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { detail?: string; title?: string };
    return new ApiError(problem.detail ?? problem.title ?? `Error ${response.status}`, response.status);
  } catch {
    return new ApiError(`Error ${response.status}`, response.status);
  }
}

export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'X-VpsPanel': '1' };
  let payload: BodyInit | undefined;
  if (body instanceof FormData) {
    payload = body;
  } else if (body !== undefined) {
    payload = JSON.stringify(body);
    headers['Content-Type'] = 'application/json';
  }
  const response = await fetch(url, { method, headers, body: payload });
  if (!response.ok) throw await toError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}
