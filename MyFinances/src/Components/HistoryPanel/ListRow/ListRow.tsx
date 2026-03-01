import { CiEdit, CiEraser } from "react-icons/ci";
import type { Transaction } from "../../../Models/Transaction";
import { IoIosArrowDown } from "react-icons/io";
import { KebabMenu, type MenuItem } from "../../../Controls/KebabMenu";

type Props = {
  transaction: Transaction;
  onDeleteTransaction: (id: number) => void;
  onEditStart: (id: number) => void;
};

const ListRow = ({ transaction, onDeleteTransaction, onEditStart }: Props) => {
  const menuItems: MenuItem[] = [
    { label: "Edit", icon: <CiEdit className="text-blue-700 text-2xl" />, onClick: () => onEditStart(transaction.id) },
    { label: "Delete", icon: <CiEraser className="text-blue-700 text-2xl" />, onClick: () => onDeleteTransaction(transaction.id) },
  ];

  return (
    <div className="grid [grid-template-columns:2.5fr_1fr_1fr_0.1fr] gap-4 mx-2 my-2 items-center border-b border-gray-200 pb-2">
      <div>
        <p className="text-blue-700 text-md flex-row flex items-center">
          {transaction.otherSideOfTransaction}
          <span className="text-gray-500 ml-auto"> {transaction.category}</span>
          <IoIosArrowDown className="ml-0.5 text-gray-500" />
        </p>
        <p className="text-xs text-gray-500">{transaction.description}</p>
      </div>
      <div className="text-center">
        <p className="text-blue-700 text-sm">
          {transaction.date.toLocaleDateString("pl-PL", {
            day: "2-digit",
            month: "2-digit",
          })}
        </p>
        <p className="text-xs text-gray-500">{transaction.bankAccount}</p>
      </div>
      <div className="text-right">
        <p className={`font-bold text-lg ${transaction.amount > 0 ? "text-green-600" : "text-red-700"}`}>
          {transaction.amount.toLocaleString("pl-PL", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          })}
        </p>
        <p className="text-[11px] text-gray-500">PLN</p>
      </div>
      <div>
        <KebabMenu items={menuItems} />
      </div>
    </div>
  );
};

export default ListRow;
