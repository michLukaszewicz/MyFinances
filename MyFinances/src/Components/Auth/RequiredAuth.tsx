import React from "react";
import { Navigate } from "react-router-dom";

type Props = {
  children?: React.ReactNode;
};

const RequiredAuth = ({ children }: Props) => {
    const token = localStorage.getItem("access_token");
    if (!token) {
        return <Navigate to="/login" replace />;
    }
  return children;
};

export default RequiredAuth;
