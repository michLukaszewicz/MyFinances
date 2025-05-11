import type { Transaction } from "../../../Models/Transaction";

type Props = {
  transaction: Transaction;
};

const ListRow = ({ transaction }: Props) => {
  return (
    <div className="grid [grid-template-columns:2.5fr_1fr_1fr] gap-4 mx-2 my-2 items-center border-b border-gray-200 pb-2">
      <div>
        <p className="text-blue-700 text-md">
          {transaction.otherAccount}
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
        <p className="text-xs text-gray-500">{transaction.account}</p>
      </div>
      <div className="text-right">
        <p
          className={`font-bold text-lg ${
            transaction.amount > 0 ? "text-green-600" : "text-red-700"
          }`}
        >
          {transaction.amount.toLocaleString("pl-PL", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          })}
        </p>
        <p className="text-[11px] text-gray-500">PLN</p>
      </div>
    </div>
  );
};

export default ListRow;
