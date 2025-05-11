import type { Transaction } from "../../../Models/Transaction";

type Props = {
  transaction: Transaction;
};

const ListRow = ({ transaction }: Props) => {
  return (
    <div className="grid [grid-template-columns:3fr_1.5fr_1fr] gap-4 mx-2 my-2 items-center border-b border-gray-200 pb-2">
      <div>
        <p className="font-medium text-blue-700 text-lg">
          {transaction.otherAccount}
        </p>
        <p className="text-xs text-gray-500">{transaction.description}</p>
      </div>
      <div className="text-center">
        <p className="font-medium text-blue-700">
          {transaction.date.toLocaleDateString("pl-PL", {day: '2-digit', month: '2-digit'})}
        </p>
        <p className="text-xs text-gray-500">{transaction.account}</p>
      </div>
      <div className="text-right">
        <p className="font-extrabold text-xl">{transaction.amount}</p>
        <p className="text-[11px] text-gray-500">PLN</p>
      </div>
    </div>
  );
};

export default ListRow;
