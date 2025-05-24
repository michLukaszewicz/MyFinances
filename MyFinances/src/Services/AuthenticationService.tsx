export const isAuthenticated = (): boolean => {
  const token = localStorage.getItem("access_token");
  return !!token;
};

export const logout = (): void => {
  localStorage.removeItem("access_token");
  window.location.href = "/login";
};
