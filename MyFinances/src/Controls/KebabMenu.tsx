import { useEffect, useRef, useState } from "react";
import { VscKebabVertical } from "react-icons/vsc";

interface Props {
  items: MenuItem[];
}

export interface MenuItem {
  label: string;
  icon: React.ReactNode;
  onClick: () => void;
}

export const KebabMenu = ({ items }: Props) => {
  const [isOpen, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  return (
    <div className="relative" ref={ref}>
      <button title="More actions" onClick={() => setOpen((v) => !v)}>
        <VscKebabVertical />
      </button>
      {isOpen && (
        <ul role="menu" className="flex flex-col gap-2 absolute bg-white p-2.5 rounded-md border-1 border-gray-300 shadow-xl right-2 top-4 z-1 w-40">
          {items.map((item, key) => (
            <li key={key} role="menuitem" className="">
              <button
                className="flex flex-row border-l-2 border-transparent hover:border-blue-500 hover:bg-blue-50 transition-all w-full"
                onClick={() => {
                  item.onClick();
                  setOpen(false);
                }}>
                {item.icon}
                <p className="ml-3">{item.label}</p>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};
