import React from "react";

interface Props {
  header: string;
  children?: React.ReactNode;
}

function Box({ header, children }: Props) {
  return (
    <div className="bg-white rounded-2xl drop-shadow-lg p-6 w-full max-w-7xl mx-auto">
        <h1 className="font-bold text-2xl mb-3">{header}</h1>
      {children}
    </div>
  );
}

export default Box;
