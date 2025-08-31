import React from "react";
import { Navigate } from "react-router-dom";

type Props = {
  children?: React.ReactNode;
  requireLoggedOut: boolean;
  redirectTo?: string;
};

const RequiredAuth: React.FC<Props> = ({ children, requireLoggedOut, redirectTo }) => {
const params = new URLSearchParams(window.location.search);
const queryToken = params.get("access_token");
const token = localStorage.getItem("access_token") || queryToken;

if (queryToken && !localStorage.getItem("access_token")) {
  localStorage.setItem("access_token", queryToken);
}
  const isLoggedIn = !!token;
  if (isLoggedIn && IsTokenExpired(token)) {
    localStorage.removeItem("access_token");
    return <Navigate to="/login" replace />;
  }

  if (requireLoggedOut && isLoggedIn) {
    return <Navigate to={redirectTo == null ? "/dashboard" : redirectTo} replace />;
  }

  if (!requireLoggedOut && !isLoggedIn) {
    return <Navigate to={redirectTo == null ? "/login" : redirectTo} replace />;
  }

  return <>{children}</>;
};

function IsTokenExpired(token: string | null) {
  if (!token) return true;

  const payload = JSON.parse(atob(token.split(".")[1]));
  const exp = payload.exp * 1000;
  return Date.now() > exp;
}

export default RequiredAuth;
