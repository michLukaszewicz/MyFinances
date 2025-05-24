import React from "react";
import { Navigate } from "react-router-dom";

type Props = {
  children?: React.ReactNode;
  requireLoggedOut: boolean;
};

const RequiredAuth = ({ children, requireLoggedOut }: Props) => {
  const token = localStorage.getItem("access_token");
  const isLoggedIn = !!token;

  if (requireLoggedOut && isLoggedIn) {
    return <Navigate to="/" replace />;
  }

  if (!requireLoggedOut && !isLoggedIn) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
};

export default RequiredAuth;
