import React from 'react'
import './Box.css'

interface Props {
    children?: React.ReactNode
}

function Box({children}: Props) {
  return (
    <div className="bg-white rounded-2xl drop-shadow-lg p-6 max-w-dvh mx-auto">
        {children}
        </div>
  )
}

export default Box