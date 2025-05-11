import React from "react";

interface Props {
  children?: React.ReactNode;
}

function Box({ children }: Props) {
  return (
    <div className="bg-white rounded-2xl drop-shadow-lg p-6 w-full max-w-7xl mx-auto">
      {children}
    </div>
  );
}

export default Box;
