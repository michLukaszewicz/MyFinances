import Box from "../../Box/Box";
import { Cell, Pie, PieChart, ResponsiveContainer } from "recharts";

type Props = {};

const data = [
  { name: "Group A", value: 400, label: "25% (2000)" },
  { name: "Group B", value: 300, label: "25% (2000)" },
  { name: "Group C", value: 300, label: "25% (2000)" },
  { name: "Group D", value: 200, label: "25% (2000)" },
];

const COLORS = ["#0088FE", "#00C49F", "#FFBB28", "#FF8042"];

const CategoryChart = (props: Props) => {
  return (
    <Box>
      <div className="flex justify-between mb-3">
        <p className="font-bold text-2xl mb-3">Category Chart</p>
        <div>
          {/* dodać tutaj możliwość wyłączeina wybranych kategorii i wybranie okresu  */}
          <p className="text-blue-600 text-sm text-right">Settings</p>
        </div>
      </div>
      <div className="flex flex-col items-center">
        <ResponsiveContainer maxHeight={200} width="95%" aspect={4.0 / 3.0} className="mx-4 mr-15">
          <PieChart width={400} height={400}>
            <Pie data={data} labelLine={true} label={({ index }) => data[index].label} outerRadius={80} fill="#8884d8" dataKey="value">
              {data.map((entry, index) => (
                <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
      </div>
    </Box>
  );
};

export default CategoryChart;
